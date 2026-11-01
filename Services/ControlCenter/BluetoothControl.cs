// Adapted from Holm (https://github.com/ademerci/Holm), WinNotch/BluetoothControl.cs and
// WinNotch/BluetoothBattery.cs, MIT License, Copyright (c) 2026 ademerci.
// See docs/THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace VNotch.Services.ControlCenter;

internal sealed partial class BluetoothDevice
{
    public string Address = "";
    public string Name = "";
    public string? FallbackName;
    public int Battery = -1;
    public bool? Connected;
    public uint ClassOfDevice;
    /// <summary>Groups the device with its other parts, its sound devices among them.</summary>
    public Guid ContainerId;
    /// <summary>Windows has the device itself, not only one of its services (it is paired).</summary>
    public bool Paired;
    /// <summary>A sound device the notch can connect and disconnect.</summary>
    public bool CanConnect;
    /// <summary>
    /// Other entries Windows keeps for the same device under the same name (classic and LE audio,
    /// one per earbud). Connecting or disconnecting covers all of them.
    /// </summary>
    public List<BluetoothDevice> Parts = new();

    public bool IsConnected => Connected ?? true;

    /// <summary>Segoe Fluent Icons glyph for the kind of device.</summary>
    public string Glyph
    {
        get
        {
            uint major = (ClassOfDevice >> 8) & 0x1F;
            if (major == 0x04) return "\uE7F6"; // audio / video -> headphones
            if (major == 0x05)
            {
                uint minor = (ClassOfDevice >> 6) & 0x3;
                if (minor == 2) return "\uE962"; // pointing device
                return ((ClassOfDevice >> 2) & 0xF) == 2 ? "\uE7FC" : "\uE765"; // gamepad : keyboard
            }
            if (major == 0x02) return "\uE8EA"; // phone

            string n = Name.ToLowerInvariant();
            if (HeadphoneName().IsMatch(n)) return "\uE7F6";
            if (MouseName().IsMatch(n)) return "\uE962";
            if (KeyboardName().IsMatch(n)) return "\uE765";
            if (GamepadName().IsMatch(n)) return "\uE7FC";
            return "\uE702";
        }
    }

    [GeneratedRegex("buds|pods|head|kopfh|ear|anc|rays|moondrop|wh-|wf-|bose|beats|jbl|sony|sennheiser|jabra|speaker|box")]
    private static partial Regex HeadphoneName();

    [GeneratedRegex("mouse|maus|^mx ")]
    private static partial Regex MouseName();

    [GeneratedRegex("keyboard|tastatur|keys")]
    private static partial Regex KeyboardName();

    [GeneratedRegex("controller|xbox|dualsense|gamepad")]
    private static partial Regex GamepadName();
}

/// <summary>
/// Lists paired Bluetooth devices and connects or disconnects headphones and speakers, like the buttons in
/// Windows' Bluetooth settings. Mice, keyboards and phones connect by themselves: Windows gives apps no way
/// to do that for them. Everything here is a local lookup or a driver call; run it off the UI thread.
/// </summary>
internal static partial class BluetoothControl
{
    private const string LogTag = "BLUETOOTH-CONTROL";

    /// <summary>Every paired device by name, each with whether the notch can connect it.</summary>
    public static List<BluetoothDevice> Paired()
    {
        var filters = new List<(string Id, Guid Container)>();
        try { ForEachFilter((id, container, _) => filters.Add((id, container))); }
        catch (Exception ex) { RuntimeLog.Warn(LogTag, $"Sound devices unavailable: {ex.Message}"); }

        var paired = new List<BluetoothDevice>();
        foreach (var device in ScanAll())
        {
            if (!device.Paired) continue;
            foreach (var (id, container) in filters)
            {
                if (Belongs(id, container, device)) { device.CanConnect = true; break; }
            }
            paired.Add(device);
        }
        return MergeByName(paired);
    }

    /// <summary>
    /// One row per name: earbuds and LE audio devices show up as several paired entries. The row is connected
    /// when any part is, and shows the lowest battery so a flat earbud isn't hidden.
    /// </summary>
    internal static List<BluetoothDevice> MergeByName(List<BluetoothDevice> devices)
    {
        var merged = new List<BluetoothDevice>(devices.Count);
        var byName = new Dictionary<string, BluetoothDevice>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var device in devices)
        {
            if (!byName.TryGetValue(device.Name, out var row))
            {
                byName[device.Name] = device;
                merged.Add(device);
                continue;
            }

            if (row.Parts.Count == 0) row.Parts.Add(Copy(row));
            row.Parts.Add(device);
            row.Connected = row.IsConnected || device.IsConnected;
            row.CanConnect |= device.CanConnect;
            if (device.Battery >= 0 && (row.Battery < 0 || device.Battery < row.Battery)) row.Battery = device.Battery;
            if (row.ClassOfDevice == 0) row.ClassOfDevice = device.ClassOfDevice;
        }
        return merged;

        static BluetoothDevice Copy(BluetoothDevice d) => new()
        {
            Address = d.Address,
            Name = d.Name,
            Battery = d.Battery,
            Connected = d.Connected,
            ClassOfDevice = d.ClassOfDevice,
            ContainerId = d.ContainerId,
            Paired = d.Paired,
            CanConnect = d.CanConnect
        };
    }

    /// <summary>
    /// Asks Windows to connect (or disconnect) all of the device's sound parts, music and calls. True when the
    /// driver took the request; the device itself connects a moment later, if it's on and in reach.
    /// </summary>
    public static bool SetConnected(BluetoothDevice device, bool connect)
    {
        int asked = 0;
        try
        {
            ForEachFilter((id, container, part) =>
            {
                if (!BelongsToAny(id, container, device)) return;
                object? control = null;
                try
                {
                    Guid iid = typeof(IKsControl).GUID;
                    if (part.Activate(ClsCtxAll, ref iid, out control) != 0) return;
                    var property = new KsProperty { Set = BtAudioProperties, Id = connect ? OneShotReconnect : OneShotDisconnect, Flags = KsPropertyTypeGet };
                    int hr = ((IKsControl)control).KsProperty(ref property, Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _);
                    if (hr == 0) asked++;
                    else RuntimeLog.Warn(LogTag, $"{(connect ? "Connect" : "Disconnect")} refused: 0x{hr:X8}");
                }
                finally { Release(control); }
            });
        }
        catch (Exception ex) { RuntimeLog.Warn(LogTag, $"Couldn't reach the Bluetooth sound driver: {ex.Message}"); }
        return asked > 0;
    }

    /// <summary>
    /// Whether a sound driver filter is one of the device's: Bluetooth music filters carry the device's address in
    /// their id, call (hands-free) filters don't, but all of them share the device's container.
    /// </summary>
    internal static bool Belongs(string filterId, Guid container, BluetoothDevice device)
        => (device.Address.Length == 12 && filterId.Contains(device.Address, StringComparison.OrdinalIgnoreCase))
        || (container != Guid.Empty && container == device.ContainerId);

    private static bool BelongsToAny(string filterId, Guid container, BluetoothDevice device)
    {
        if (device.Parts.Count == 0) return Belongs(filterId, container, device);
        foreach (var part in device.Parts)
            if (Belongs(filterId, container, part)) return true;
        return false;
    }

    /// <summary>Bluetooth classic and hands-free filters ("{2}.\\?\bthenum#…", "{2}.\\?\bthhfenum#…").</summary>
    internal static bool IsBluetooth(string filterId) => filterId.Contains(@"\\?\bth", StringComparison.OrdinalIgnoreCase);

    #region Paired devices (SetupAPI)

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData { public int Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid FormatId; public uint PropertyId;
        public DevPropKey(string guid, uint id) { FormatId = new Guid(guid); PropertyId = id; }
    }

    private const uint DigcfPresent = 0x2, DigcfAllClasses = 0x4;
    private const uint DevPropTypeByte = 0x3, DevPropTypeUInt32 = 0x7, DevPropTypeGuid = 0xD, DevPropTypeBoolean = 0x11, DevPropTypeString = 0x12;

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr parent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SpDevInfoData data);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetupDiGetDeviceInstanceId(IntPtr set, ref SpDevInfoData data, char* id, int size, out int required);

    private static unsafe string ReadInstanceId(IntPtr set, ref SpDevInfoData data, char[] buffer)
    {
        fixed (char* id = buffer)
        {
            return SetupDiGetDeviceInstanceId(set, ref data, id, buffer.Length, out int len)
                ? new string(buffer, 0, Math.Max(0, len - 1)) : "";
        }
    }

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceProperty(IntPtr set, ref SpDevInfoData data, ref DevPropKey key, out uint type, [Out] byte[] buffer, int size, out int required, uint flags);

    private static DevPropKey KeyBattery = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
    private static DevPropKey KeyAddress = new("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A", 1);
    private static DevPropKey KeyClassOfDevice = new("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A", 10);
    private static DevPropKey KeyFriendlyName = new("A45C254E-DF1C-4EFD-8020-67D146A850E0", 14);
    private static DevPropKey KeyDeviceDesc = new("A45C254E-DF1C-4EFD-8020-67D146A850E0", 2);
    private static DevPropKey KeyContainerId = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C", 2);
    // Not in the public headers, but it is what the Bluetooth settings page uses for "Connected".
    private static DevPropKey KeyConnected = new("83DA6326-97A6-4088-9453-A1923F573B29", 15);

    private static readonly string[] Enumerators = ["BTHENUM", "BTHLE", "BTHLEDEVICE"];
    private static readonly string[] NoiseSuffixes = [" Hands-Free AG", " Hands-Free", " Avrcp Transport", " Stereo"];

    [GeneratedRegex(@"(?:DEV_|&|_)([0-9A-F]{12})(?:_|\\|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AddressInId();

    /// <summary>Every paired device, connected or not, and any other one that reports a battery level, by name.</summary>
    internal static List<BluetoothDevice> ScanAll()
    {
        var byAddress = new Dictionary<string, BluetoothDevice>(StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[1024];
        var idBuffer = new char[512];

        foreach (var enumerator in Enumerators)
        {
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, enumerator, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) continue;
            try
            {
                var data = new SpDevInfoData { Size = Marshal.SizeOf<SpDevInfoData>() };
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    string id = ReadInstanceId(set, ref data, idBuffer);
                    string? address = ReadString(set, ref data, ref KeyAddress, buffer);
                    if (address == null && AddressInId().Match(id) is { Success: true } m) address = m.Groups[1].Value;
                    if (string.IsNullOrEmpty(address) || address.Trim('0').Length == 0) continue; // the PC's own services

                    if (!byAddress.TryGetValue(address, out var dev))
                        byAddress[address] = dev = new BluetoothDevice { Address = address.ToUpperInvariant() };

                    bool isDeviceNode = id.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase)
                                     || id.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase);
                    if (isDeviceNode)
                    {
                        dev.Name = ReadString(set, ref data, ref KeyFriendlyName, buffer)
                                ?? ReadString(set, ref data, ref KeyDeviceDesc, buffer) ?? dev.Name;
                        if (ReadBool(set, ref data, ref KeyConnected, buffer) is bool connected) dev.Connected = connected;
                        if (ReadUInt(set, ref data, ref KeyClassOfDevice, buffer) is uint cod) dev.ClassOfDevice = cod;
                        if (ReadGuid(set, ref data, ref KeyContainerId, buffer) is Guid container) dev.ContainerId = container;
                        dev.Paired = true;
                    }
                    else if (dev.FallbackName == null && ReadString(set, ref data, ref KeyFriendlyName, buffer) is string n)
                    {
                        dev.FallbackName = Clean(n);
                    }

                    if (ReadByte(set, ref data, ref KeyBattery, buffer) is byte level && level <= 100)
                        dev.Battery = level;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }

        var result = new List<BluetoothDevice>(byAddress.Count);
        foreach (var d in byAddress.Values)
        {
            if (!d.Paired && d.Battery < 0) continue;
            if (d.Name.Length == 0) d.Name = d.FallbackName ?? "Bluetooth";
            result.Add(d);
        }
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    private static string Clean(string name)
    {
        foreach (var suffix in NoiseSuffixes)
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name[..^suffix.Length];
        return name;
    }

    private static bool Read(IntPtr set, ref SpDevInfoData data, ref DevPropKey key, byte[] buffer, out uint type, out int size)
        => SetupDiGetDeviceProperty(set, ref data, ref key, out type, buffer, buffer.Length, out size, 0);

    private static string? ReadString(IntPtr set, ref SpDevInfoData data, ref DevPropKey key, byte[] buffer)
    {
        if (!Read(set, ref data, ref key, buffer, out uint type, out int size) || type != DevPropTypeString || size < 2) return null;
        string s = Encoding.Unicode.GetString(buffer, 0, size).TrimEnd('\0');
        return s.Length == 0 ? null : s;
    }

    private static byte? ReadByte(IntPtr set, ref SpDevInfoData data, ref DevPropKey key, byte[] buffer)
        => Read(set, ref data, ref key, buffer, out uint type, out int size) && type == DevPropTypeByte && size >= 1 ? buffer[0] : null;

    private static bool? ReadBool(IntPtr set, ref SpDevInfoData data, ref DevPropKey key, byte[] buffer)
        => Read(set, ref data, ref key, buffer, out uint type, out int size) && type == DevPropTypeBoolean && size >= 1 ? buffer[0] != 0 : null;

    private static Guid? ReadGuid(IntPtr set, ref SpDevInfoData data, ref DevPropKey key, byte[] buffer)
        => Read(set, ref data, ref key, buffer, out uint type, out int size) && type == DevPropTypeGuid && size >= 16 ? new Guid(buffer.AsSpan(0, 16)) : null;

    private static uint? ReadUInt(IntPtr set, ref SpDevInfoData data, ref DevPropKey key, byte[] buffer)
        => Read(set, ref data, ref key, buffer, out uint type, out int size) && type == DevPropTypeUInt32 && size >= 4 ? BitConverter.ToUInt32(buffer, 0) : null;

    #endregion

    #region Bluetooth sound driver (Core Audio device topology)

    // Core Audio's map of a sound device: the endpoint (what the Sound settings list) is wired to the
    // driver's filter, and that filter answers the Bluetooth audio driver's properties.
    // Created by class id, not with a [ComImport] class: Windows hands out one enumerator, and a typed
    // "new" fails with InvalidCastException once another part of the app (MicrophoneControl) wrapped it first.
    private static readonly Type EnumeratorType =
        Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid FormatId; public int PropertyId; }

    /// <summary>Only the parts we read: the type, and a pointer in the first field.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Value;
    }

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        // Only the start of the table is declared: nothing after it is called.
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        [PreserveSig] int GetConnectorType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector other);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector other);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPart
    {
        // Only the place in the table matters for the ones we never call.
        [PreserveSig] int GetName();
        [PreserveSig] int GetLocalId();
        [PreserveSig] int GetGlobalId();
        [PreserveSig] int GetPartType();
        [PreserveSig] int GetSubType();
        [PreserveSig] int GetControlInterfaceCount();
        [PreserveSig] int GetControlInterface();
        [PreserveSig] int EnumPartsIncoming();
        [PreserveSig] int EnumPartsOutgoing();
        [PreserveSig] int GetTopologyObject();
        [PreserveSig] int Activate(int clsContext, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty property, int propertyLength, IntPtr data, int dataLength, out int bytesReturned);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KsProperty { public Guid Set; public uint Id; public uint Flags; }

    // KSPROPSETID_BtAudio from bthdef.h: "connect again" and "disconnect", what the Bluetooth settings page sends.
    private static readonly Guid BtAudioProperties = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private const uint OneShotReconnect = 0, OneShotDisconnect = 1, KsPropertyTypeGet = 1;
    private const int ClsCtxAll = 0x17;

    private static PropertyKey ContainerIdKey = new() { FormatId = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), PropertyId = 2 };

    /// <summary>
    /// Visits each Bluetooth sound driver filter once, whether its device is connected or not, with its id, its
    /// container and the part to talk to. The part is released afterwards.
    /// </summary>
    private static void ForEachFilter(Action<string, Guid, IPart> visit)
    {
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(EnumeratorType)!;
        IMMDeviceCollection? devices = null;
        try
        {
            if (enumerator.EnumAudioEndpoints(2 /* eAll */, 0xF /* DEVICE_STATEMASK_ALL */, out devices) != 0 || devices.GetCount(out int count) != 0)
                return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) != 0) continue;
                object? topology = null;
                IConnector? connector = null, other = null;
                try
                {
                    Guid iid = typeof(IDeviceTopology).GUID;
                    if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out topology) != 0) continue;
                    if (((IDeviceTopology)topology).GetConnector(0, out connector) != 0) continue;
                    if (connector.GetDeviceIdConnectedTo(out string filterId) != 0 || !IsBluetooth(filterId) || !seen.Add(filterId)) continue;
                    if (connector.GetConnectedTo(out other) != 0) continue;
                    visit(filterId, ContainerOf(device), (IPart)other);
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException)
                {
                    // Not every endpoint exposes a topology; skip it.
                }
                finally
                {
                    Release(other);
                    Release(connector);
                    Release(topology);
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            Release(devices);
            // The enumerator stays: the same one serves MicrophoneControl.
        }
    }

    private static Guid ContainerOf(IMMDevice device)
    {
        if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0) return Guid.Empty;
        try
        {
            if (store.GetValue(ref ContainerIdKey, out var value) != 0) return Guid.Empty;
            try { return value.Type == 72 /* VT_CLSID */ && value.Value != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(value.Value) : Guid.Empty; }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    private static void Release(object? com)
    {
        if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    #endregion
}
