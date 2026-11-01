using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using VNotch.Controls;
using VNotch.Services.ClaudeCode;
using VNotch.Services.ControlCenter;
using Xunit;

namespace VNotch.Tests;

public class ControlCenterServicesTests
{
    private const int NetworkSize = 628;

    [Fact]
    public void ReadNetworks_KeepsOneRowPerName_PrefersTheConnectedOne_AndSkipsHiddenNetworks()
    {
        IntPtr list = Marshal.AllocHGlobal(8 + 4 * NetworkSize);
        try
        {
            var zero = new byte[8 + 4 * NetworkSize];
            Marshal.Copy(zero, 0, list, zero.Length);
            Marshal.WriteInt32(list, 4);
            WriteNetwork(list, 0, "Home", profile: null, signal: 80, secured: true, flags: 0);
            WriteNetwork(list, 1, "Home", profile: "Home", signal: 80, secured: true, flags: 0x1 | 0x2);
            WriteNetwork(list, 2, "", profile: null, signal: 90, secured: false, flags: 0);
            WriteNetwork(list, 3, "Café", profile: null, signal: 40, secured: false, flags: 0);

            var networks = new List<WifiNetwork>();
            NetworkControl.ReadNetworks(list, networks);

            Assert.Equal(2, networks.Count);
            Assert.Equal("Home", networks[0].Ssid);
            Assert.True(networks[0].IsConnected);
            Assert.Equal("Home", networks[0].ProfileName);
            Assert.True(networks[0].Secured);
            Assert.Equal("Café", networks[1].Ssid);
            Assert.False(networks[1].HasProfile);
            Assert.Equal(40, networks[1].Signal);
        }
        finally
        {
            Marshal.FreeHGlobal(list);
        }
    }

    private static void WriteNetwork(IntPtr list, int index, string ssid, string? profile, int signal, bool secured, int flags)
    {
        IntPtr item = list + 8 + index * NetworkSize;
        if (profile != null)
        {
            var name = Encoding.Unicode.GetBytes(profile + "\0");
            Marshal.Copy(name, 0, item, name.Length);
        }
        var ssidBytes = Encoding.UTF8.GetBytes(ssid);
        Marshal.WriteInt32(item, 512, ssidBytes.Length);
        if (ssidBytes.Length > 0) Marshal.Copy(ssidBytes, 0, item + 516, ssidBytes.Length);
        Marshal.WriteInt32(item, 604, signal);
        Marshal.WriteInt32(item, 608, secured ? 1 : 0);
        Marshal.WriteInt32(item, 620, flags);
    }

    [Theory]
    [InlineData("VirtualBox Host-Only Ethernet Adapter", true)]
    [InlineData("Tailscale Tunnel", true)]
    [InlineData("Bluetooth Device (Personal Area Network)", true)]
    [InlineData("ASIX USB to Gigabit Ethernet Family Adapter", false)]
    [InlineData("Intel(R) Ethernet Connection I219-V", false)]
    public void IsVirtual_LeavesOutSwitchesTunnelsAndBluetooth(string description, bool expected)
    {
        Assert.Equal(expected, NetworkControl.IsVirtual(description));
    }

    [Fact]
    public void BluetoothFilters_MatchByAddressOrContainer()
    {
        var container = Guid.NewGuid();
        var device = new BluetoothDevice { Address = "A1B2C3D4E5F6", ContainerId = container };

        Assert.True(BluetoothControl.Belongs(@"{2}.\\?\bthenum#{0000110b}_vid&0002_a1b2c3d4e5f6#", Guid.Empty, device));
        Assert.True(BluetoothControl.Belongs(@"{2}.\\?\bthhfenum#bthhfpaudio#6&1", container, device));
        Assert.False(BluetoothControl.Belongs(@"{2}.\\?\bthhfenum#bthhfpaudio#6&1", Guid.NewGuid(), device));
        Assert.True(BluetoothControl.IsBluetooth(@"{2}.\\?\bthenum#x"));
        Assert.False(BluetoothControl.IsBluetooth(@"{2}.\\?\hdaudio#func_01"));
    }

    [Fact]
    public void PairedEntriesWithTheSameName_BecomeOneRow()
    {
        var classic = new BluetoothDevice { Address = "A1B2C3D4E5F6", Name = "Buds2 Pro", Battery = 92, Connected = false, CanConnect = true };
        var leftBud = new BluetoothDevice { Address = "111111111111", Name = "Buds2 Pro", Battery = 100, Connected = true };
        var other = new BluetoothDevice { Address = "222222222222", Name = "WH-CH520", Battery = 70, Connected = false, CanConnect = true };

        var rows = BluetoothControl.MergeByName([classic, leftBud, other]);

        Assert.Equal(2, rows.Count);
        var buds = rows[0];
        Assert.True(buds.IsConnected);
        Assert.True(buds.CanConnect);
        Assert.Equal(92, buds.Battery);
        Assert.Equal(2, buds.Parts.Count);
        Assert.Empty(rows[1].Parts);
    }

    [Fact]
    public void UsageParse_ReadsBothWindows()
    {
        var limits = ClaudeUsageClient.Parse("""
            { "five_hour": { "utilization": 42.5, "resets_at": "2026-10-31T15:00:00Z" },
              "seven_day": { "utilization": 7, "resets_at": null } }
            """);

        Assert.NotNull(limits);
        Assert.Equal(42.5, limits!.FiveHour!.UsedPercent);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 15, 0, 0, TimeSpan.Zero), limits.FiveHour.ResetsAt);
        Assert.Equal(7, limits.Week!.UsedPercent);
        Assert.Null(limits.Week.ResetsAt);
        Assert.Null(ClaudeUsageClient.Parse("{}"));
    }

    [Theory]
    [InlineData(59, "1 min")]
    [InlineData(25 * 60, "25 min")]
    [InlineData(2 * 3600 + 13 * 60, "2 h 13 min")]
    [InlineData(3 * 86400 + 5 * 3600, "3 d 5 h")]
    public void FormatDuration_IsShort(int seconds, string expected)
    {
        Assert.Equal(expected, ControlCenterView.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void FormatElapsed_LooksLikeAStopwatch()
    {
        Assert.Equal("0:07", ControlCenterView.FormatElapsed(TimeSpan.FromSeconds(7)));
        Assert.Equal("12:05", ControlCenterView.FormatElapsed(TimeSpan.FromSeconds(12 * 60 + 5)));
        Assert.Equal("1:02:03", ControlCenterView.FormatElapsed(new TimeSpan(1, 2, 3)));
    }
}
