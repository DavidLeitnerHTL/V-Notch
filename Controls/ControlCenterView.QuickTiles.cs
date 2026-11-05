using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VNotch.Services;
using VNotch.Services.ControlCenter;

namespace VNotch.Controls;

// Quick tiles under the system stats, like macOS Control Center: Wi-Fi, Bluetooth, sound and the mic at a
// glance and one click away. The detailed sections below start folded; their headers open them.
public partial class ControlCenterView
{
    private static readonly Brush QuickIdleBadge = Frozen("#22FFFFFF");
    private static readonly Brush QuickSound = Frozen("#BF5AF2");
    private readonly UniformGrid _quickTiles = new() { Columns = 4, Rows = 1, Margin = new Thickness(-3, 10, -3, 2) };
    private string _quickKey = "";

    private static bool FoldedByDefault(string section) => section is "network" or "bluetooth" or "audio" or "notifications";

    private void BuildQuickTilesIfChanged(Snapshot s)
    {
        string? ssid = s.Networks.FirstOrDefault(n => n.IsConnected)?.Ssid;
        bool wired = s.Wired.Any(w => w.IsUp);
        var device = s.Devices.FirstOrDefault(d => d.Connected == true);
        var output = s.Outputs.FirstOrDefault(d => d.IsDefault);
        string key = $"{s.WifiOn}|{ssid}|{wired}|{s.BluetoothOn}|{device?.Name}|{output?.Id}|{s.Outputs.Count}|{s.MicMuted}|{s.MicName}";
        if (key == _quickKey) return;
        _quickKey = key;

        _quickTiles.Children.Clear();
        _quickTiles.Children.Add(WifiTile(s, ssid, wired));
        _quickTiles.Children.Add(BluetoothTile(s, device));
        _quickTiles.Children.Add(SoundTile(s, output));
        _quickTiles.Children.Add(MicTile(s));
    }

    private Border WifiTile(Snapshot s, string? ssid, bool wired)
    {
        string subtitle = s.WifiOn switch
        {
            true => ssid ?? (wired ? Loc.Get("control.wired") : Loc.Get("control.notConnected")),
            false => wired ? Loc.Get("control.wired") : Loc.Get("control.off"),
            _ => Loc.Get("control.noAdapter"),
        };
        Action? toggle = s.WifiOn is bool on && _wifiRadio is { } radio ? () => SetRadio(radio, !on) : null;
        return QuickTile("", Loc.Get("control.wifi"), subtitle, s.WifiOn == true ? Blue : null, toggle);
    }

    private Border BluetoothTile(Snapshot s, BluetoothDevice? device)
    {
        string subtitle = s.BluetoothOn switch
        {
            true => device?.Name is { Length: > 0 } name ? name : Loc.Get("control.on"),
            false => Loc.Get("control.off"),
            _ => Loc.Get("control.noAdapter"),
        };
        Action? toggle = s.BluetoothOn is bool on && _bluetoothRadio is { } radio ? () => SetRadio(radio, !on) : null;
        return QuickTile("", "Bluetooth", subtitle, s.BluetoothOn == true ? Blue : null, toggle);
    }

    private Border SoundTile(Snapshot s, AudioDeviceInfo? output)
    {
        var outputs = s.Outputs;
        Border? tile = null;
        tile = QuickTile("", Loc.Get("control.audio"), output?.FriendlyName ?? Loc.Get("control.noAdapter"),
            output != null ? QuickSound : null,
            outputs.Count > 0 ? () => ShowOutputMenu(tile!, outputs, output) : null);
        return tile;
    }

    private Border MicTile(Snapshot s)
    {
        bool live = s.MicMuted == false;
        string subtitle = s.MicMuted switch
        {
            false => s.MicName is { Length: > 0 } name ? name : Loc.Get("control.on"),
            true => Loc.Get("control.off"),
            _ => Loc.Get("control.noAdapter"),
        };
        Action? toggle = s.MicMuted is bool muted ? () => SetMicrophoneMuted(!muted) : null;
        return QuickTile(live ? "" : "", Loc.Get("control.microphone"), subtitle, live ? Orange : null, toggle);
    }

    /// <summary>A round badge that fills with its colour while the thing is on, a title and what it is doing.</summary>
    private Border QuickTile(string glyph, string title, string subtitle, Brush? activeTint, Action? onClick)
    {
        bool active = activeTint != null;
        var badge = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = activeTint ?? QuickIdleBadge,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = SegoeSymbolFont,
                FontSize = 14,
                Foreground = active ? Brushes.White : Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var name = MakeLabel(title, 12.5, UiPalette.PrimaryBrush);
        name.FontWeight = FontWeights.SemiBold;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var detail = MakeLabel(subtitle, 11, Muted);
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        detail.Margin = new Thickness(0, 1, 0, 0);
        var text = new StackPanel { Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { name, detail } };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(badge, Dock.Left);
        row.Children.Add(badge);
        row.Children.Add(text);

        var card = Card();
        card.Padding = new Thickness(10, 9, 10, 9);
        card.Margin = new Thickness(3, 0, 3, 0);
        card.Child = row;
        card.ToolTip = $"{title}: {subtitle}";
        System.Windows.Automation.AutomationProperties.SetName(card, title);
        if (onClick != null)
        {
            card.Cursor = Cursors.Hand;
            Pressable(card, () => CardBg, CardHover);
            card.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                onClick();
            };
        }
        return card;
    }
}
