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
    private WaveXlrState? _waveXlr;
    private readonly LoadWarningState _loadWarning = new();
    private int _compactDisplayRevision;
    private bool _readingCompactDisplays;
    private void BuildCompactSystem(Snapshot s)
    {
        _accent = Frozen("#8FA9C4");
        if (!_compactSystemReady)
        {
            _compactSystemReady = true;
            MountCompactPage();
            BuildSystemFace();
        }
        // Sound is only read while the tab is visible; a read without it keeps the last mic state instead of
        // flashing "not found" when the tab opens.
        if (s.Outputs != null)
        {
            _outputs = s.Outputs;
            _waveXlr = s.WaveXlr;
            _micMuted = s.WaveXlr == null ? s.MicMuted : null;
        }
        PaintFaceRow(_faceWifi, s.WifiOn, s.Networks.FirstOrDefault(n => n.IsConnected)?.Ssid ?? Say("An", "On"), Say("Aus", "Off"), ConnectionBlue);
        PaintFaceRow(_faceBluetooth, s.BluetoothOn, Say("An", "On"), Say("Aus", "Off"), ConnectionBlue);
        PaintMic();
        ShowFaceOutput();
        ShowFaceMode();
        RefreshQuickPage(s);
        var current = _volume?.Current ?? (0f, false);
        ShowCompactVolume(current.Item1, current.Item2);
        FillSystemDrawer(s);
        if (s.Stats is { } stats && IsVisible)
            SetCompactStatus(_loadWarning.Update(DateTime.UtcNow, stats.CpuPercent, stats.GpuPercent, _legion?.CpuTemp, _legion?.GpuTemp));
    }

    private static string PercentAndTemp(double? percent, int? temp) =>
        (percent is { } p ? $"{p:0} %" : "–") + (temp is > 0 ? $", {temp} °C" : "");

    /// <summary>Wave Link's own state when it knows the XLR, otherwise the default mic's mute flag from Windows.</summary>
    private void PaintMic() => PaintFaceRow(_faceMic,
        _waveXlr != null ? !_waveXlr.Muted : _micMuted is { } muted ? !muted : null,
        _waveXlr != null ? "Wave XLR" : Say("An", "On"), Say("Stumm", "Muted"), MicSand);

    private async void ToggleWaveXlr()
    {
        if (_faceMic is not { } mic) return;
        ((FrameworkElement)mic.Disc.Parent).IsEnabled = false;
        if (_waveXlr is { } state)
        {
            bool muted = !state.Muted;
            bool changed = await WaveXlrControl.SetMutedAsync(state, muted);
            if (_disposed) return;
            if (changed) _waveXlr = state with { Muted = muted };
        }
        else if (_micMuted is { } current)
        {
            // Windows' mute works for ordinary mics; the XLR ignores it, and the next read shows what really happened.
            bool target = !current;
            if (await Task.Run(() => MicrophoneControl.SetMuted(target)) && !_disposed) _micMuted = target;
            if (!_disposed) RefreshSoon(400);
        }
        if (!_disposed) PaintMic();
    }

    private bool? _micMuted;

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

    private void SelectCompactOutput(OutputDevice output)
    {
        if (output.IsDefault) return;
        _outputs = _outputs.Select(o => o with { IsDefault = o.Id == output.Id }).ToList();
        ShowFaceOutput();
        if (_snapshot is { } snapshot)
        {
            snapshot.Outputs = _outputs;
            BuildCompactSystem(snapshot);
        }
        Task.Run(() => AudioMixerService.SwitchDefaultOutputDevice(output.Id));
        RefreshSoon(350);
    }

    private void ShowCompactVolume(float volume, bool muted) => ShowCapsuleLevel(_volumeCapsule, muted ? 0 : volume);

    /// <summary>
    /// Reads every active screen's brightness (DDC, so it is slow and runs off the UI thread): the drawer's screen
    /// slider and the Bildschirme page show them.
    /// </summary>
    private async void RefreshCompactDisplays()
    {
        if (_readingCompactDisplays || _disposed || !IsVisible) return;
        _readingCompactDisplays = true;
        int revision = ++_compactDisplayRevision;
        try
        {
            var displays = await Task.Run(() => DisplayApi.List());
            if (_disposed || !IsVisible || revision != _compactDisplayRevision) return;
            var levels = new Dictionary<string, int?>();
            foreach (var display in displays.Where(d => d.Active))
            {
                int? level = DisplayApi is Win32DisplayApi native ? await native.GetBrightnessAsync(display.Id) : await DisplayBrightness.GetAsync(display.Id);
                if (_disposed || !IsVisible || revision != _compactDisplayRevision) return;
                levels[display.Id] = level;
            }
            _drawerDisplayList = displays;
            _monitorLevels = levels;
            FillScreenSlider();
            if (_drawerPage == DrawerPage.Displays) PaintScreenTiles();
            RefitDrawer();
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
