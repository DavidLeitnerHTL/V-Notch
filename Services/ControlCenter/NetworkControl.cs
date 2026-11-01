using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace VNotch.Services.ControlCenter;

/// <summary>A Wi-Fi network in reach. <paramref name="Signal"/> is 0–100.</summary>
internal sealed record WifiNetwork(string Ssid, string? ProfileName, int Signal, bool Secured, bool IsConnected)
{
    /// <summary>Windows has a saved profile, so it can connect without asking for a password.</summary>
    public bool HasProfile => !string.IsNullOrEmpty(ProfileName);
}

/// <summary>A wired adapter. Virtual switches, VPN tunnels and Bluetooth PAN are left out.</summary>
internal sealed record WiredAdapter(string Name, string Description, bool IsUp);

internal enum WifiListStatus
{
    Ok,
    NoAdapter,
    /// <summary>Windows 11 hides Wi-Fi networks from desktop apps without location access.</summary>
    LocationBlocked,
    Failed
}

/// <summary>
/// Wired and Wi-Fi status and switching between known Wi-Fi networks through the native WLAN API
/// (wlanapi.dll), the same one the network flyout uses. No admin rights needed.
/// </summary>
internal static partial class NetworkControl
{
    private const string LogTag = "NETWORK";

    private static readonly string[] VirtualAdapterHints =
        ["virtual", "hyper-v", "vmware", "vpn", "tap-", "tunnel", "wireguard", "tailscale", "zerotier", "bluetooth", "loopback", "npcap"];

    public static List<WiredAdapter> GetWiredAdapters()
    {
        var result = new List<WiredAdapter>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                    or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit))
                    continue;
                if (IsVirtual(nic.Description) || IsVirtual(nic.Name)) continue;
                result.Add(new WiredAdapter(nic.Name, nic.Description, nic.OperationalStatus == OperationalStatus.Up));
            }
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(LogTag, $"Adapter list failed: {ex.Message}");
        }
        return result;
    }

    internal static bool IsVirtual(string text)
    {
        foreach (var hint in VirtualAdapterHints)
        {
            if (text.Contains(hint, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Networks in reach, the connected one first, then by signal. Run it off the UI thread.</summary>
    public static WifiListStatus GetNetworks(List<WifiNetwork> networks, bool rescan)
    {
        networks.Clear();
        if (!TryOpen(out IntPtr client)) return WifiListStatus.Failed;
        try
        {
            if (!TryGetFirstInterface(client, out Guid adapter)) return WifiListStatus.NoAdapter;
            if (rescan) _ = WlanScan(client, ref adapter, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

            int hr = WlanGetAvailableNetworkList(client, ref adapter, IncludeAllManualHiddenProfiles, IntPtr.Zero, out IntPtr list);
            if (hr == ErrorAccessDenied) return WifiListStatus.LocationBlocked;
            if (hr != 0) return WifiListStatus.Failed;
            try
            {
                ReadNetworks(list, networks);
            }
            finally
            {
                WlanFreeMemory(list);
            }
            networks.Sort(CompareNetworks);
            return WifiListStatus.Ok;
        }
        finally
        {
            _ = WlanCloseHandle(client, IntPtr.Zero);
        }
    }

    /// <summary>Connects with Windows' saved profile for that network.</summary>
    public static bool Connect(string profileName)
    {
        if (!TryOpen(out IntPtr client)) return false;
        IntPtr profile = Marshal.StringToHGlobalUni(profileName);
        try
        {
            if (!TryGetFirstInterface(client, out Guid adapter)) return false;
            var parameters = new WlanConnectionParameters
            {
                Mode = 0, // wlan_connection_mode_profile
                Profile = profile,
                BssType = 1 // dot11_BSS_type_infrastructure
            };
            int hr = WlanConnect(client, ref adapter, ref parameters, IntPtr.Zero);
            if (hr != 0) RuntimeLog.Warn(LogTag, $"Connect refused: {hr}");
            return hr == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(profile);
            _ = WlanCloseHandle(client, IntPtr.Zero);
        }
    }

    public static bool Disconnect()
    {
        if (!TryOpen(out IntPtr client)) return false;
        try
        {
            return TryGetFirstInterface(client, out Guid adapter) && WlanDisconnect(client, ref adapter, IntPtr.Zero) == 0;
        }
        finally
        {
            _ = WlanCloseHandle(client, IntPtr.Zero);
        }
    }

    /// <summary>Windows' own list of networks, for ones that still need a password.</summary>
    public static void OpenNetworkFlyout() => OpenUri("ms-availablenetworks:");

    public static void OpenLocationSettings() => OpenUri("ms-settings:privacy-location");

    private static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(LogTag, $"Couldn't open {uri}: {ex.Message}");
        }
    }

    private static int CompareNetworks(WifiNetwork a, WifiNetwork b)
    {
        if (a.IsConnected != b.IsConnected) return a.IsConnected ? -1 : 1;
        if (a.HasProfile != b.HasProfile) return a.HasProfile ? -1 : 1;
        return b.Signal.CompareTo(a.Signal);
    }

    // WLAN_AVAILABLE_NETWORK (wlanapi.h): 628 bytes, read by offset to keep the fixed arrays out of marshalling.
    private const int NetworkSize = 628;
    private const int SsidLengthOffset = 512;
    private const int SsidOffset = 516;
    private const int SignalOffset = 604;
    private const int SecurityOffset = 608;
    private const int FlagsOffset = 620;
    private const int FlagConnected = 0x1;
    private const int FlagHasProfile = 0x2;

    internal static void ReadNetworks(IntPtr list, List<WifiNetwork> networks)
    {
        int count = Marshal.ReadInt32(list);
        var bySsid = new Dictionary<string, int>(StringComparer.Ordinal);
        var ssidBytes = new byte[32];
        for (int i = 0; i < count; i++)
        {
            IntPtr item = list + 8 + i * NetworkSize;
            int ssidLength = Math.Clamp(Marshal.ReadInt32(item, SsidLengthOffset), 0, 32);
            if (ssidLength == 0) continue; // hidden network
            Marshal.Copy(item + SsidOffset, ssidBytes, 0, ssidLength);
            string ssid = Encoding.UTF8.GetString(ssidBytes, 0, ssidLength);
            int flags = Marshal.ReadInt32(item, FlagsOffset);
            string? profile = (flags & FlagHasProfile) != 0 ? Marshal.PtrToStringUni(item) : null;
            var network = new WifiNetwork(
                ssid,
                string.IsNullOrEmpty(profile) ? null : profile,
                Math.Clamp(Marshal.ReadInt32(item, SignalOffset), 0, 100),
                Marshal.ReadInt32(item, SecurityOffset) != 0,
                (flags & FlagConnected) != 0);

            // One row per name: Windows lists a network once per profile and once without.
            if (bySsid.TryGetValue(ssid, out int existing))
            {
                var old = networks[existing];
                bool better = (network.IsConnected && !old.IsConnected) ||
                              (network.HasProfile && !old.HasProfile && !old.IsConnected);
                if (better) networks[existing] = network;
                continue;
            }
            bySsid[ssid] = networks.Count;
            networks.Add(network);
        }
    }

    private static bool TryOpen(out IntPtr client)
    {
        client = IntPtr.Zero;
        try
        {
            return WlanOpenHandle(2, IntPtr.Zero, out _, out client) == 0;
        }
        catch (DllNotFoundException)
        {
            return false; // No WLAN service on this edition.
        }
    }

    private static bool TryGetFirstInterface(IntPtr client, out Guid adapter)
    {
        adapter = Guid.Empty;
        if (WlanEnumInterfaces(client, IntPtr.Zero, out IntPtr list) != 0) return false;
        try
        {
            if (Marshal.ReadInt32(list) == 0) return false;
            adapter = Marshal.PtrToStructure<Guid>(list + 8);
            return true;
        }
        finally
        {
            WlanFreeMemory(list);
        }
    }

    private const int ErrorAccessDenied = 5;
    private const uint IncludeAllManualHiddenProfiles = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanConnectionParameters
    {
        public int Mode;
        public IntPtr Profile;
        public IntPtr Ssid;
        public IntPtr DesiredBssidList;
        public int BssType;
        public uint Flags;
    }

    [LibraryImport("wlanapi.dll")]
    private static partial int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [LibraryImport("wlanapi.dll")]
    private static partial int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [LibraryImport("wlanapi.dll")]
    private static partial int WlanScan(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr ssid, IntPtr ieData, IntPtr reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial int WlanGetAvailableNetworkList(IntPtr clientHandle, ref Guid interfaceGuid, uint flags, IntPtr reserved, out IntPtr networkList);

    [LibraryImport("wlanapi.dll")]
    private static partial int WlanConnect(IntPtr clientHandle, ref Guid interfaceGuid, ref WlanConnectionParameters parameters, IntPtr reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial int WlanDisconnect(IntPtr clientHandle, ref Guid interfaceGuid, IntPtr reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial void WlanFreeMemory(IntPtr memory);
}
