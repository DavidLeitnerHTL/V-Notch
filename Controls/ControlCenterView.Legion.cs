using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VNotch.Services;

namespace VNotch.Controls;

/// <summary>
/// The <see cref="ControlCenterPage.Legion"/> page: what Lenovo Vantage does on a Legion that matters at
/// the desk — the power mode (Fn+Q), the Windows key and touchpad locks, CPU and GPU temperature.
/// </summary>
public partial class ControlCenterView
{
    #region Legion

    private static readonly TimeSpan LegionRefreshInterval = TimeSpan.FromSeconds(2);

    // The colours the power button glows in for each mode.
    private static readonly (LegionMode Mode, string Glyph, string Key, Brush Glow)[] LegionModes =
    [
        (LegionMode.Quiet, "", "legion.quiet", Frozen("#0A84FF")),
        (LegionMode.Balanced, "", "legion.balanced", Frozen("#F2F2F2")),
        (LegionMode.Performance, "", "legion.performance", Frozen("#FF453A")),
        (LegionMode.Extreme, "", "legion.extreme", Frozen("#BF5AF2")),
    ];

    private DispatcherTimer? _legionTimer;
    private LegionState? _legion;
    private bool _legionReachable = true;
    private bool _legionBusy;
    private string _legionKey = "";
    private readonly StackPanel _legionSection = new();

    /// <summary>For the <see cref="ControlCenterPage.Legion"/> page.</summary>
    internal void AttachLegion()
    {
        ControlRoot.Children.Add(_legionSection);
        _legionTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = LegionRefreshInterval };
        _legionTimer.Tick += (_, _) => RefreshLegion();
    }

    private void OnLegionVisibleChanged(bool visible)
    {
        if (visible)
        {
            BuildLegionPage();
            RefreshLegion();
            _legionTimer?.Start();
        }
        else
        {
            _legionTimer?.Stop();
        }
    }

    private async void RefreshLegion()
    {
        if (_legionBusy || _disposed) return;
        _legionBusy = true;
        try
        {
            ApplyLegion(await LegionService.GetStateAsync());
        }
        finally
        {
            _legionBusy = false;
        }
    }

    private void ApplyLegion(LegionState? state)
    {
        if (_disposed) return;
        _legionReachable = state != null;
        if (state != null) _legion = state;
        BuildLegionPage();
    }

    private void BuildLegionPage()
    {
        if (_disposed) return;
        var state = _legion;
        string key = $"{_legionReachable}|{state}|{IsExpanded("legion.mode")}|{IsExpanded("legion.keys")}";
        if (key == _legionKey) return;
        _legionKey = key;

        _legionSection.Children.Clear();
        string temps = state is { CpuTemp: > 0 } ? Loc.Get("legion.temps", state.CpuTemp, state.GpuTemp) : "";
        _legionSection.Children.Add(BuildSectionHeader("legion.mode", Loc.Get("legion.mode"), temps, null, topMargin: 0));

        if (!_legionReachable || state == null)
        {
            _legionSection.Children.Add(NewRow("", Loc.Get(_legionReachable ? "pi.loading" : "legion.setupHint"), muted: true));
            if (!_legionReachable) _legionSection.Children.Add(LegionSetupTile());
            return;
        }

        if (IsExpanded("legion.mode"))
        {
            var modeGrid = TileGrid(LegionModes.Length, LegionModes.Length);
            for (int i = 0; i < LegionModes.Length; i++)
            {
                var (mode, glyph, name, glow) = LegionModes[i];
                bool active = state.Mode == (int)mode;
                PlaceTile(modeGrid, LegionTile(glyph, Loc.Get(name), Loc.Get(active ? "legion.active" : "legion.tapToSet"), active, glow,
                    () => LegionService.SetModeAsync(mode)), i, LegionModes.Length);
            }
            _legionSection.Children.Add(modeGrid);
            if (state.Mode == (int)LegionMode.Custom)
                _legionSection.Children.Add(NewRow("", Loc.Get("legion.custom"), muted: true));
        }

        _legionSection.Children.Add(BuildSectionHeader("legion.keys", Loc.Get("legion.keys"), "", null, topMargin: 6));
        if (!IsExpanded("legion.keys")) return;
        var keyGrid = TileGrid(PiSwitchColumns, 2);
        bool winKeyOn = !state.WinKeyLocked, touchpadOn = !state.TouchpadLocked;
        PlaceTile(keyGrid, LegionTile("", Loc.Get("legion.winKey"), Loc.Get(winKeyOn ? "control.on" : "legion.locked"), winKeyOn, Green,
            () => LegionService.SetWinKeyLockedAsync(winKeyOn)), 0, PiSwitchColumns);
        PlaceTile(keyGrid, LegionTile("", Loc.Get("legion.touchpad"), Loc.Get(touchpadOn ? "control.on" : "legion.locked"), touchpadOn, Green,
            () => LegionService.SetTouchpadLockedAsync(touchpadOn)), 1, PiSwitchColumns);
        _legionSection.Children.Add(keyGrid);
    }

    /// <summary>A tile like the Pi switches: lit in <paramref name="glow"/> while active; a tap runs <paramref name="apply"/>.</summary>
    private FrameworkElement LegionTile(string glyphText, string label, string stateText, bool active, Brush glow,
        Func<Task<LegionState?>> apply)
    {
        var glyph = new TextBlock { Text = glyphText, FontFamily = SegoeSymbolFont, FontSize = 16, Foreground = active ? glow : Muted };
        var name = MakeLabel(label, 12.5, VNotch.Services.UiPalette.PrimaryBrush);
        name.Margin = new Thickness(0, 7, 0, 0);
        var state = MakeLabel(stateText, 11, Muted);
        state.Margin = new Thickness(0, 1, 0, 0);
        var stack = new StackPanel();
        stack.Children.Add(glyph);
        stack.Children.Add(name);
        stack.Children.Add(state);

        Brush litBg = glow.Clone();
        litBg.Opacity = 0.2;
        litBg.Freeze();
        var card = Card();
        card.Padding = new Thickness(12, 10, 12, 10);
        card.Cursor = Cursors.Hand;
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = new ScaleTransform(1, 1);
        card.Background = active ? litBg : CardBg;
        card.Child = stack;
        System.Windows.Automation.AutomationProperties.SetName(card, label);
        card.MouseEnter += (_, _) => { if (!active) card.Background = CardHover; };
        card.MouseLeave += (_, _) => card.Background = active ? litBg : CardBg;
        card.MouseLeftButtonDown += async (_, e) =>
        {
            e.Handled = true;
            Spring(card);
            card.Background = litBg;
            glyph.Foreground = glow;
            _legionBusy = true; // no poll may paint the old state over the tap
            try
            {
                ApplyLegion(await apply());
            }
            finally
            {
                _legionBusy = false;
            }
        };
        return card;
    }

    private FrameworkElement LegionSetupTile()
    {
        var label = MakeLabel(Loc.Get("legion.setup"), 12.5, VNotch.Services.UiPalette.PrimaryBrush);
        var card = Card();
        card.Cursor = Cursors.Hand;
        card.HorizontalAlignment = HorizontalAlignment.Left;
        card.Margin = new Thickness(0, 4, 0, 0);
        card.Child = label;
        card.MouseEnter += (_, _) => card.Background = CardHover;
        card.MouseLeave += (_, _) => card.Background = CardBg;
        card.MouseLeftButtonDown += async (_, e) =>
        {
            e.Handled = true;
            label.Text = Loc.Get("legion.settingUp");
            await LegionService.RunSetupAsync();
            _legionKey = "";
            RefreshLegion();
        };
        return card;
    }

    #endregion
}
