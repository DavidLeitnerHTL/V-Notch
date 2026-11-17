using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VNotch.Services;
using VNotch.Services.ControlCenter;
using VNotch.Services.Displays;

namespace VNotch.Controls;

public partial class ControlCenterView
{
    private bool _compactSystemReady;
    private Grid? _systemModeName, _systemOutputName;
    private TextBlock? _systemOutputIcon;
    private Border? _systemOutputChip, _systemModeThumb;
    private readonly TranslateTransform _systemModeShift = new();
    private readonly List<TextBlock> _systemModeIcons = new();
    private readonly List<(ScaleTransform Fill, TextBlock Value)> _systemMasterBars = new();
    private readonly Dictionary<uint, (ScaleTransform Fill, TextBlock Value)> _systemAppBars = new();
    private readonly StackPanel _systemSound = new(), _systemReadouts = new(), _systemDevices = new(), _systemDisplays = new();
    private TextBlock? _systemCpu, _systemGpu, _systemRam, _systemTraffic;
    private string _systemDrawerKey = "";
    private int _systemSelectedMode = -1;
    private (Border Button, TextBlock Icon)? _systemWifi, _systemBluetooth, _systemMic;
    private WaveXlrState? _waveXlr;
    private readonly LoadWarningState _loadWarning = new();
    private Border? _advancedDisplays;
    private bool _advancedDisplaysOpen;
    private int _compactDisplayRevision;
    private bool _readingCompactDisplays;
    private const double SystemModeStep = 34;

    private static readonly Color[] SystemModeColors =
    [ Color.FromRgb(0x8F, 0xA9, 0xC4), Color.FromRgb(0xE8, 0xE6, 0xEF), Color.FromRgb(0xC9, 0x8A, 0x84), Color.FromRgb(0xA6, 0xA2, 0xC8) ];
    private static readonly int[] SystemModeGlyphs = [0xE8BE, 0xEC49, 0xECAD, 0xE945];

    private void BuildCompactSystem(Snapshot s)
    {
        _accent = Frozen("#8FA9C4");
        if (!_compactSystemReady)
        {
            _compactSystemReady = true;
            MountCompactPage();
            BuildSystemBar();
        }
        if (s.Outputs != null) _outputs = s.Outputs;
        _waveXlr = s.WaveXlr;
        PaintSystemToggle(_systemWifi, s.WifiOn, Color.FromRgb(0x8F, 0xA9, 0xC4));
        PaintSystemToggle(_systemBluetooth, s.BluetoothOn, Color.FromRgb(0x8F, 0xA9, 0xC4));
        PaintSystemToggle(_systemMic, _waveXlr == null ? null : !_waveXlr.Muted, Color.FromRgb(0xC9, 0xB0, 0x8E));
        if (_systemMic is { } mic) mic.Button.ToolTip = _waveXlr == null ? "Wave XLR nicht erreichbar" : _waveXlr.Muted ? "Wave XLR: stumm" : "Wave XLR";
        ShowCompactOutput();
        ShowCompactMode();
        var current = _volume?.Current ?? (0f, false);
        ShowCompactVolume(current.Item1, current.Item2);
        string key = string.Join(";", (s.Apps ?? new()).Select(a => $"{a.ProcessId}:{a.DisplayName}")) + "|" +
            string.Join(";", _outputs.Select(o => $"{o.Id}:{o.Name}:{o.IsDefault}:{o.Bluetooth}")) + "|" +
            string.Join(";", s.Devices.Select(d => $"{d.Address}:{d.Name}:{d.Battery}:{d.IsConnected}")) + "|" +
            s.WifiOn + s.BluetoothOn + string.Join(";", s.Networks.Select(n => $"{n.Ssid}:{n.IsConnected}")) +
            _legionReachable + (_legion != null) + _legion?.WinKeyLocked + _legion?.TouchpadLocked +
            _keepAwakeMode?.Invoke() + _phone?.Invoke()?.State;
        if (key != _systemDrawerKey) { _systemDrawerKey = key; BuildSystemDrawer(s); }
        if (s.Stats is { } stats)
        {
            _systemCpu!.Text = PercentAndTemp(stats.CpuPercent, _legion?.CpuTemp);
            _systemGpu!.Text = PercentAndTemp(stats.GpuPercent, _legion?.GpuTemp);
            _systemRam!.Text = $"{stats.RamUsedGb:0.#} von {stats.RamTotalGb:0.#} GB";
            _systemTraffic!.Text = $"↓ {FormatRate(stats.DownBytesPerSecond)}  ↑ {FormatRate(stats.UpBytesPerSecond)}";
            if (IsVisible) SetCompactStatus(_loadWarning.Update(DateTime.UtcNow, stats.CpuPercent, stats.GpuPercent, _legion?.CpuTemp, _legion?.GpuTemp));
        }
        if (s.Apps != null)
            foreach (var app in s.Apps)
                if (_systemAppBars.TryGetValue(app.ProcessId, out var bar)) PaintCompactBar(bar, app.Volume, app.IsMuted);
        UpdateTopList(s.Top);
    }

    private static string PercentAndTemp(double? percent, int? temp) =>
        (percent is { } p ? $"{p:0} %" : "–") + (temp is > 0 ? $", {temp} °C" : "");

    private void BuildSystemBar()
    {
        var row = new Grid { MinHeight = 32 };
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (_legionAttached) row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        if (_legionAttached)
        {
            var modes = new Grid { Width = 4 + 3 * SystemModeStep + 26, Height = 30 };
            var thumb = _systemModeThumb = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0), RenderTransform = _systemModeShift,
                Background = new SolidColorBrush(SystemModeColors[1]), IsHitTestVisible = false };
            modes.Children.Add(thumb);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var icon = CompactGlyph(SystemModeGlyphs[i]); _systemModeIcons.Add(icon);
                var button = new Border { Width = 26, Height = 28, HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(2 + SystemModeStep * i, 0, 0, 0), Background = Brushes.Transparent, CornerRadius = new CornerRadius(7),
                    Child = icon, Cursor = Cursors.Hand, ToolTip = Loc.Get(LegionModes[i].Key) };
                Squish(button);
                button.MouseLeftButtonUp += async (_, e) =>
                {
                    e.Handled = true;
                    if (_legionBusy || _systemSelectedMode == index) return;
                    _legionBusy = true;
                    try
                    {
                        if (await LegionService.SetModeAsync(LegionModes[index].Mode) is { } state) ApplyLegion(state);
                    }
                    finally { _legionBusy = false; }
                };
                modes.Children.Add(button);
            }
            var segment = new Border { Background = CompactTrack, CornerRadius = new CornerRadius(9), Child = modes };
            _systemModeName = new Grid { Width = 76, Height = 28, Margin = new Thickness(12, 0, 0, 0), ClipToBounds = true };
            var block = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 20, 12), Children = { segment, _systemModeName } };
            Grid.SetColumnSpan(block, 3);
            row.Children.Add(block);
        }
        _systemOutputName = new Grid { Width = 46, Height = 22, ClipToBounds = true };
        _systemOutputIcon = CompactGlyph(0xE7F5);
        _systemOutputChip = new Border { Height = 25, CornerRadius = new CornerRadius(8), Background = CompactTrack,
            Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 0, 16, 0), Cursor = Cursors.Hand,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _systemOutputIcon, _systemOutputName } } };
        Pressable(_systemOutputChip, () => CompactTrack, CardHover);
        _systemOutputChip.MouseLeftButtonUp += (_, e) => { e.Handled = true; CycleCompactOutput(); };
        Grid.SetRow(_systemOutputChip, _legionAttached ? 1 : 0);
        row.Children.Add(_systemOutputChip);
        var volume = CompactVolumeBar(0, v => _volume?.SetVolume((float)v), master: true);
        volume.MinWidth = 160;
        Grid.SetRow(volume, _legionAttached ? 1 : 0);
        Grid.SetColumn(volume, 1); Grid.SetColumnSpan(volume, _legionAttached ? 3 : 2); row.Children.Add(volume);
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, _legionAttached ? 12 : 0) };
        _systemWifi = SystemRoundToggle(0xE701, "WLAN", () => { if (_wifiRadio != null) SetRadio(_wifiRadio, _snapshot?.WifiOn != true); });
        _systemBluetooth = SystemRoundToggle(0xE702, "Bluetooth", () => { if (_bluetoothRadio != null) SetRadio(_bluetoothRadio, _snapshot?.BluetoothOn != true); });
        _systemMic = SystemRoundToggle(0xE720, "Wave XLR", ToggleWaveXlr);
        foreach (var toggle in new[] { _systemWifi.Value, _systemBluetooth.Value, _systemMic.Value }) toggles.Children.Add(toggle.Button);
        _systemMic.Value.Button.Margin = new Thickness(0);
        Grid.SetColumn(toggles, 3); row.Children.Add(toggles);
        _compactPage.Children.Add(row);
    }

    private (Border Button, TextBlock Icon) SystemRoundToggle(int glyph, string tooltip, Action action)
    {
        var icon = CompactGlyph(glyph);
        var button = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = CompactTrack,
            Margin = new Thickness(0, 0, 12, 0), Child = icon, Cursor = Cursors.Hand, ToolTip = tooltip };
        Pressable(button, () => button.Tag is true ? Frozen("#263F4D5B") : CompactTrack, CardHover);
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; action(); };
        return (button, icon);
    }

    private static void PaintSystemToggle((Border Button, TextBlock Icon)? parts, bool? on, Color color)
    {
        if (parts is not { } p) return;
        bool changed = p.Button.Tag is bool previous && previous != on;
        p.Button.Tag = on;
        p.Button.IsEnabled = on != null;
        p.Button.Opacity = on == null ? .4 : 1;
        p.Button.Background = on == true ? new SolidColorBrush(Alpha(color, .24)) : CompactTrack;
        p.Icon.Foreground = on == true ? new SolidColorBrush(color) : CompactLabel;
        if (changed && p.Icon.IsVisible && !AnimationConfig.ReduceMotion)
        {
            var scale = FeedbackScale(p.Icon);
            p.Icon.RenderTransformOrigin = new Point(.5, .5);
            Motion.To(scale, ScaleTransform.ScaleXProperty, 1, Motion.Pop, from: .6);
            Motion.To(scale, ScaleTransform.ScaleYProperty, 1, Motion.Pop, from: .6);
        }
    }

    private async void ToggleWaveXlr()
    {
        if (_waveXlr is not { } state || _systemMic is not { } mic) return;
        mic.Button.IsEnabled = false;
        bool muted = !state.Muted;
        bool changed = await WaveXlrControl.SetMutedAsync(state, muted);
        if (_disposed) return;
        if (changed) _waveXlr = state with { Muted = muted };
        PaintSystemToggle(_systemMic, _waveXlr == null ? null : !_waveXlr.Muted, Color.FromRgb(0xC9, 0xB0, 0x8E));
    }

    private void ShowCompactMode()
    {
        if (_systemModeName == null || _systemModeThumb == null) return;
        int index = _legion == null ? 1 : Array.FindIndex(LegionModes, m => (int)m.Mode == _legion.Mode);
        int visual = Math.Max(0, index);
        string name = _legion == null ? "–" : LegionModeLook(_legion.Mode).Name;
        SwapCompactLabel(_systemModeName, name, direction: index >= _systemSelectedMode ? 1 : -1, size: 12);
        if (_systemSelectedMode != index)
        {
            if (_systemSelectedMode < 0 || !_systemModeThumb.IsVisible) Motion.Stop(_systemModeShift, TranslateTransform.XProperty, visual * SystemModeStep);
            else Motion.To(_systemModeShift, TranslateTransform.XProperty, visual * SystemModeStep, Motion.Shift);
            _systemModeThumb.Background = new SolidColorBrush(SystemModeColors[visual]);
            _systemSelectedMode = index;
        }
        _systemModeThumb.Opacity = _legion == null || index < 0 ? .25 : 1;
        for (int i = 0; i < _systemModeIcons.Count; i++) _systemModeIcons[i].Foreground = i == index ? Frozen("#161616") : CompactLabel;
    }

    internal static string ShortOutputName(OutputDevice output)
    {
        if (output.FriendlyName.Contains("Wave", StringComparison.OrdinalIgnoreCase) || output.FriendlyName.Contains("Elgato Virtual Audio", StringComparison.OrdinalIgnoreCase)) return "Wave";
        if (output.FriendlyName.Contains("AirPods", StringComparison.OrdinalIgnoreCase)) return "AirPods";
        return output.Name.Length > 12 ? output.Name[..12] : output.Name;
    }

    internal static int OutputGlyph(OutputDevice? output) => output != null &&
        (output.Bluetooth || output.FriendlyName.Contains("Headphone", StringComparison.OrdinalIgnoreCase) ||
            output.FriendlyName.Contains("Kopfh", StringComparison.OrdinalIgnoreCase) ||
            output.FriendlyName.Contains("AirPods", StringComparison.OrdinalIgnoreCase)) ? 0xE7F6 : 0xE7F5;

    private void ShowCompactOutput()
    {
        if (_systemOutputChip == null || _systemOutputName == null) return;
        var output = _outputs.FirstOrDefault(o => o.IsDefault);
        _systemOutputChip.ToolTip = output?.FriendlyName ?? Loc.Get("control.noAdapter");
        _systemOutputChip.IsEnabled = _outputs.Count > 1;
        if (_systemOutputIcon != null) _systemOutputIcon.Text = Glyph(OutputGlyph(output));
        SwapCompactLabel(_systemOutputName, output == null ? "–" : ShortOutputName(output));
    }

    private void CycleCompactOutput()
    {
        if (_outputs.Count < 2) return;
        int index = _outputs.FindIndex(o => o.IsDefault);
        SelectCompactOutput(_outputs[(index + 1) % _outputs.Count]);
    }

    private void SelectCompactOutput(OutputDevice output)
    {
        if (output.IsDefault) return;
        _outputs = _outputs.Select(o => o with { IsDefault = o.Id == output.Id }).ToList();
        ShowCompactOutput();
        if (_snapshot is { } snapshot)
        {
            snapshot.Outputs = _outputs;
            BuildCompactSystem(snapshot);
        }
        Task.Run(() => AudioMixerService.SwitchDefaultOutputDevice(output.Id));
        RefreshSoon(350);
    }

    private Grid CompactVolumeBar(double level, Action<double> set, bool master = false, uint? app = null)
    {
        var value = CompactText($"{level * 100:0} %", 11, CompactLabel); value.Width = 34; value.TextAlignment = TextAlignment.Right;
        var track = ThinBar(level, CompactInk, v => { value.Text = $"{v * 100:0} %"; set(v); });
        var fill = (ScaleTransform)track.Tag;
        var grid = new Grid { MinHeight = 27 };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(track); Grid.SetColumn(value, 1); grid.Children.Add(value);
        track.PreviewMouseWheel += (_, e) => { e.Handled = true; double v = Math.Clamp(fill.ScaleX + Math.Sign(e.Delta) * WheelVolumeStep, 0, 1); set(v); Motion.Stop(fill, ScaleTransform.ScaleXProperty, v); value.Text = $"{v * 100:0} %"; };
        if (master) _systemMasterBars.Add((fill, value));
        if (app is { } id) _systemAppBars[id] = (fill, value);
        return grid;
    }

    private void ShowCompactVolume(float volume, bool muted)
    {
        foreach (var bar in _systemMasterBars) PaintCompactBar(bar, volume, muted);
    }

    private static void PaintCompactBar((ScaleTransform Fill, TextBlock Value) bar, double level, bool muted)
    {
        double target = muted ? 0 : Math.Clamp(level, 0, 1);
        bar.Value.Text = $"{target * 100:0} %";
        bar.Value.ToolTip = muted ? "Stumm" : null;
        if (bar.Value.IsVisible) Motion.To(bar.Fill, ScaleTransform.ScaleXProperty, target, Motion.Shift);
        else Motion.Stop(bar.Fill, ScaleTransform.ScaleXProperty, target);
    }

    private void BuildSystemDrawer(Snapshot s)
    {
        foreach (var part in new FrameworkElement[] { _systemSound, _systemReadouts, _systemDevices, _systemDisplays, _displaysSection, _topList })
        {
            if (part.Parent is Panel parent) parent.Children.Remove(part);
            else if (part.Parent is Border border) border.Child = null;
        }
        _systemSound.Children.Clear(); _systemReadouts.Children.Clear(); _systemDevices.Children.Clear();
        while (_systemMasterBars.Count > 1) _systemMasterBars.RemoveAt(1);
        _systemAppBars.Clear();
        _systemSound.Children.Add(CompactText("Ton", 11, CompactLabel));
        foreach (var output in _outputs)
        {
            var row = CompactLine(OutputGlyph(output), ShortOutputName(output),
                output.IsDefault ? CompactGlyph(0xE73E, CompactInk) : null);
            var hit = new Border { Child = row, ToolTip = output.FriendlyName, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            Pressable(hit, () => Brushes.Transparent, Frozen("#0AFFFFFF"), gentle: true);
            hit.MouseLeftButtonUp += (_, e) => { e.Handled = true; SelectCompactOutput(output); };
            _systemSound.Children.Add(hit);
        }
        var master = CompactVolumeBar(_volume?.Current.Volume ?? 0, v => _volume?.SetVolume((float)v), master: true);
        _systemSound.Children.Add(CompactLine(0xE767, "Gesamt", master)); master.Width = 130;
        foreach (var session in s.Apps ?? new())
        {
            var app = session;
            var bar = CompactVolumeBar(app.Volume, v =>
            {
                if (_mixer?.Invoke() is { } mixer) Task.Run(() => mixer.SetSessionVolume(app.ProcessId, (float)v));
            }, app: app.ProcessId);
            bar.Width = 130;
            _systemSound.Children.Add(CompactLine(0xE767, app.DisplayName, bar));
        }
        _systemSound.Children.Add(CompactText("Displays", 11, CompactLabel));
        _systemSound.Children.Add(_systemDisplays);
        var more = CompactPill("Anzeigeoptionen", () =>
        {
            _advancedDisplaysOpen = !_advancedDisplaysOpen;
            _expanded["displays"] = true;
            if (_advancedDisplaysOpen) BuildDisplaysSection();
            if (_advancedDisplays != null) FoldInline(_advancedDisplays, _advancedDisplaysOpen, true);
        });
        _systemSound.Children.Add(more);
        _systemReadouts.Children.Add(CompactText("Leistung gerade", 11, CompactLabel));
        _systemCpu = CompactText("–", 11, CompactLabel); _systemGpu = CompactText("–", 11, CompactLabel); _systemRam = CompactText("–", 11, CompactLabel);
        var cpu = CompactLine(0xE950, "CPU", _systemCpu); OpenOnClick(cpu, () => "taskmgr.exe");
        _systemReadouts.Children.Add(cpu); _systemReadouts.Children.Add(CompactLine(0xE7F4, "GPU", _systemGpu));
        _systemReadouts.Children.Add(CompactLine(0xE964, "RAM", _systemRam));
        var processes = CompactPill("Prozesse", () => ToggleTop(TopMode.Cpu)); _systemReadouts.Children.Add(processes);
        _systemReadouts.Children.Add(CompactText("Geräte", 11, CompactLabel));
        _systemReadouts.Children.Add(_systemDevices);
        foreach (var device in s.Devices)
        {
            var detail = new StackPanel { Orientation = Orientation.Horizontal };
            if (device.Battery >= 0) detail.Children.Add(CompactText($"{device.Battery} %", 11, CompactLabel));
            if (device.CanConnect) detail.Children.Add(CompactPill(device.IsConnected ? "Trennen" : "Verbinden", () => SetBluetoothConnected(device, !device.IsConnected)));
            _systemDevices.Children.Add(CompactLine(device.Glyph[0], device.Name, detail));
        }
        AddSystemNetwork(s);
        if (_legionAttached)
        {
            if (_legion is { } state)
            {
                _systemDevices.Children.Add(CompactLine(0xE765, Loc.Get("legion.winKey"), Toggle(!state.WinKeyLocked, async on => ApplyLegion(await LegionService.SetWinKeyLockedAsync(!on)))));
                _systemDevices.Children.Add(CompactLine(0xEFA5, Loc.Get("legion.touchpad"), Toggle(!state.TouchpadLocked, async on => ApplyLegion(await LegionService.SetTouchpadLockedAsync(!on)))));
            }
            else _systemDevices.Children.Add(CompactPill(Loc.Get("legion.setup"), async () => { await LegionService.RunSetupAsync(); RefreshLegion(); }));
            var light = CompactLine(0xED39, "Tastaturlicht", CompactText("Fn + Leertaste", 10.5, CompactLabel));
            light.ToolTip = "Der installierte Lenovo-Helfer stellt die Tastaturbeleuchtung nicht bereit";
            _systemDevices.Children.Add(light);
        }
        if (_keepAwakeMode != null && _setKeepAwakeMode != null)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var automatic = CompactPill("Auto", () => { _setKeepAwakeMode(KeepAwakeMode.Automatic); _systemDrawerKey = ""; if (_snapshot != null) BuildCompactSystem(_snapshot); });
            automatic.ToolTip = _keepAwakeLabel?.Invoke(); actions.Children.Add(automatic);
            actions.Children.Add(Toggle(_keepAwakeMode() != KeepAwakeMode.Off, on => _setKeepAwakeMode(on ? KeepAwakeMode.Always : KeepAwakeMode.Off)));
            _systemDevices.Children.Add(CompactLine(0xE708, "Wach bleiben", actions));
        }
        AddPhoneRow(_systemDevices);
        var body = new StackPanel { Children = { CompactColumns(_systemSound, _systemReadouts) } };
        _advancedDisplays = new Border { Height = 0, ClipToBounds = true, Child = _displaysSection };
        body.Children.Add(_advancedDisplays); body.Children.Add(_topList);
        AddDrawer(body);
        if (_advancedDisplaysOpen) FoldInline(_advancedDisplays, true, false);
        if (IsVisible && _systemDisplays.Children.Count == 0) RefreshCompactDisplays();
        ContentHeightChanged?.Invoke();
    }

    private void AddSystemNetwork(Snapshot s)
    {
        Border? wifi = null;
        string name = s.Networks.FirstOrDefault(n => n.IsConnected)?.Ssid ?? Loc.Get("control.notConnected");
        wifi = CompactPill(name, () =>
        {
            if (s.WifiStatus == WifiListStatus.LocationBlocked) NetworkControl.OpenLocationSettings();
            else ShowWifiMenu(wifi!);
        });
        wifi.MaxWidth = 140;
        _systemDevices.Children.Add(CompactLine(0xE701, "WLAN", wifi));
        var wired = s.Wired.FirstOrDefault(w => w.IsUp) ?? s.Wired.FirstOrDefault();
        if (wired != null)
        {
            var row = CompactLine(0xE839, "Ethernet", CompactText(wired.IsUp ? Loc.Get("control.connected") : "–", 11, CompactLabel));
            row.ToolTip = wired.Description; _systemDevices.Children.Add(row);
        }
        _systemTraffic = CompactText("–", 10.5, CompactLabel);
        _systemDevices.Children.Add(_systemTraffic);
    }

    private async void RefreshCompactDisplays()
    {
        if (_readingCompactDisplays || _disposed || !IsVisible) return;
        _readingCompactDisplays = true;
        int revision = ++_compactDisplayRevision;
        try
        {
            var displays = await Task.Run(() => DisplayApi.List());
            if (_disposed || !IsVisible || revision != _compactDisplayRevision) return;
            _systemDisplays.Children.Clear();
            foreach (var display in displays.Where(d => d.Active))
            {
                int? level = DisplayApi is Win32DisplayApi native ? await native.GetBrightnessAsync(display.Id) : await DisplayBrightness.GetAsync(display.Id);
                if (_disposed || !IsVisible || revision != _compactDisplayRevision) return;
                if (level == null) continue;
                var bar = CompactVolumeBar(level.Value / 100d, value => DisplayBrightness.Set(display.Id, (int)Math.Round(value * 100)));
                bar.Width = 125;
                _systemDisplays.Children.Add(CompactLine(0xE706, display.Name, bar));
            }
            if (_drawerOpen && _drawerHeight != null) Motion.Stop(_drawerHeight, WeatherTween.ValueProperty, MeasureDrawer());
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { RuntimeLog.Warn("CONTROL-CENTER", $"Displays not read: {ex.Message}"); }
        finally
        {
            _readingCompactDisplays = false;
            if (!_disposed && IsVisible && revision != _compactDisplayRevision) _ = Dispatcher.BeginInvoke(new Action(RefreshCompactDisplays));
        }
    }

    internal sealed class LoadWarningState
    {
        private DateTime? _highSince, _lowSince;
        private bool _active;
        public void Pause() { _highSince = _lowSince = null; }
        public string Update(DateTime now, double? cpu, double? gpu, int? cpuTemp, int? gpuTemp)
        {
            double load = Math.Max(cpu ?? 0, gpu ?? 0);
            int temp = Math.Max(cpuTemp ?? 0, gpuTemp ?? 0);
            if (load >= 90) _highSince ??= now; else _highSince = null;
            if (!_active && (temp >= 85 || _highSince is { } high && now - high >= TimeSpan.FromSeconds(10))) _active = true;
            if (_active && load < 80 && temp < 80)
            {
                _lowSince ??= now;
                if (now - _lowSince >= TimeSpan.FromSeconds(10)) _active = false;
            }
            else _lowSince = null;
            if (!_active) return "";
            bool graphics = temp >= 85 ? (gpuTemp ?? 0) >= (cpuTemp ?? 0) : (gpu ?? 0) >= (cpu ?? 0);
            return (graphics ? "GPU " : "CPU ") + PercentAndTemp(graphics ? gpu : cpu, graphics ? gpuTemp : cpuTemp);
        }
    }
}
