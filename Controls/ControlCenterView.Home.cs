using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VNotch.Services;
using VNotch.Services.ControlCenter;

namespace VNotch.Controls;

/// <summary>The Pi-backed home tab and its extended drawer.</summary>
public partial class ControlCenterView
{
    #region Home

    // A rough household price per kWh in Austria, for the "≈ €" next to the PC's day.
    private const double EuroPerKwh = 0.25;
    private static readonly TimeSpan GuardWindow = TimeSpan.FromSeconds(3);
    private const int TargetSendDelayMs = 700;
    private const int HomeSettleMs = 1400;

    private static (Color Main, Color Warmer, Color Cooler) HomeNight => (RoomMist, RoomLilac, RoomMist);
    private static (Color Main, Color Warmer, Color Cooler) HomeLamp => (RoomSand, RoomSand, RoomMist);

    private PiHome? _home;
    private string _homeKey = "";
    private readonly LoopSet _homeLoops = new();
    private readonly StackPanel _homeLightSection = new();
    private readonly StackPanel _homeClimateSection = new();
    private readonly StackPanel _homePlugSection = new();
    private readonly StackPanel _homePiSection = new();
    private readonly Dictionary<string, TextBlock> _plugStatus = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlugReadout> _plugReadouts = new(StringComparer.Ordinal);
    private TextBlock? _humidity;
    private Action<HomeLight>? _stripDrawer;
    private TextBlock? _piLine;
    private PiReadout? _piReadout;
    private bool? _musicSync;
    private bool _homeRefreshQueued;
    private string? _guardArmed;
    private double? _targetDraft;
    private bool _targetSending;
    private DispatcherTimer? _targetSend;

    private static string Glyph(int code) => ((char)code).ToString();

    private void AttachHome()
    {
        MountCompactPage();
        _compactPage.Children.Add(_homeLightSection);
        SetMoodColors(HomeNight.Main, HomeNight.Warmer, HomeNight.Cooler);
        CoverColorsFile.Changed += OnCoverColorsChanged;
    }

    private async Task RefreshMusicSyncAsync() => _musicSync = await GoveeSync.IsRunningAsync();

    private void BuildHome()
    {
        if (_disposed) return;
        if (_isLocalOnly() || _pi?.IsConfigured != true || _home is not { } home)
        {
            string text = _isLocalOnly() ? "pi.localOnly" : _pi?.IsConfigured != true ? "pi.notConfigured" : _piReachable ? "pi.loading" : "pi.unreachable";
            string emptyKey = "empty|" + text;
            if (emptyKey == _homeKey) return;
            _homeKey = emptyKey;
            RemoveCompactDrawer();
            SetCompactStatus("");
            ClearHome();
            ClearHomeFace();
            Seen.ForgetAll("home.face.");
            _homeFaceLastSeen.Clear();
            _homeLightSection.Children.Add(text is "pi.loading" or "pi.unreachable"
                ? new SkeletonPlaceholder(!_piReachable, Loc.Get(text), 82, 56, 48, 34)
                : NewRow(Glyph(0xE7BA), Loc.Get(text), muted: true));
            return;
        }

        var mood = HomeMood(home.Lights.Select(AsShown).ToArray());
        SetMoodColors(mood.Main, mood.Warmer, mood.Cooler);
        UpdateHomeFace(home);
        string key = HomeKey(home);
        if (key != _homeKey)
        {
            _homeKey = key;
            ClearHome();
            _accent = _mood;
            if (home.Lights.FirstOrDefault(l => l.Rgb) is { } strip)
                _homeClimateSection.Children.Add(StripDrawer(strip, home.StripColors));
            BuildClimate(home);
            BuildPlugs(home);
            BuildPiLine(home);
            var right = new StackPanel { Children = { _homePlugSection, _homePiSection } };
            AddDrawer(CompactColumns(_homeClimateSection, right));
        }
        UpdateHome(home);
    }

    private void ClearHome()
    {
        foreach (var section in new[] { _homeClimateSection, _homePlugSection, _homePiSection })
            if (section.Parent is Panel parent) parent.Children.Remove(section);
        _homeLoops.StopAll();
        _plugStatus.Clear();
        _plugReadouts.Clear();
        _humidity = null;
        _stripDrawer = null;
        _piLine = null;
        _piReadout = null;
        foreach (var section in new[] { _homeClimateSection, _homePlugSection, _homePiSection })
            section.Children.Clear();
    }

    /// <summary>What changes the layout; lamps, temperatures and watts only change values in place.</summary>
    private string HomeKey(PiHome home)
    {
        var sb = new StringBuilder();
        foreach (string section in new[] { "home.lights", "home.climate", "home.plugs", "home.pi" }) sb.Append(IsExpanded(section) ? '1' : '0');
        sb.Append('|').Append(_musicSync.HasValue).Append('|');
        foreach (var l in home.Lights.Where(l => l.Rgb)) sb.Append(l.Id).Append(l.Name).Append(';');
        foreach (var color in home.StripColors) sb.Append(color.Name).Append(color.Color).Append(';');
        sb.Append('|').Append(home.Climate != null).Append(home.Climate?.WindowOpen).Append('|');
        foreach (var p in home.Plugs) sb.Append(p.Id).Append(p.Name).Append(p.Guard).Append(p.Available).Append(p.On).Append(';');
        sb.Append('|').Append(_guardArmed);
        foreach (var a in home.AirConditioner) sb.Append(a.Id);
        sb.Append('|').Append(home.Pi != null);
        if (home.Pi != null) foreach (var i in home.Pi.Issues) sb.Append(i.Title).Append(i.FixId).Append(';');
        return sb.ToString();
    }

    /// <summary>
    /// The strip's colour while it is on (muted to the tab's calm tones), warm sand with a lamp on, night indigo
    /// when the room is dark. Pass the lights through <see cref="AsShown"/> first, so music sync counts with the
    /// cover's colour.
    /// </summary>
    internal static (Color Main, Color Warmer, Color Cooler) HomeMood(IReadOnlyList<HomeLight> lights)
    {
        if (lights.FirstOrDefault(l => l.Rgb && l.On && l.Color != null) is { Color: { } strip })
        {
            var main = Calm(strip, maxSaturation: 0.3, lightness: 0.6);
            return (main, Mix(main, HomeLamp.Main, 0.5), Mix(main, HomeNight.Main, 0.5));
        }
        return lights.Any(l => l.On) ? HomeLamp : HomeNight;
    }

    /// <summary>
    /// The strip as the room sees it: with music sync and a song playing it glows in the cover's main colour (Home
    /// Assistant only knows its resting colour then); otherwise in the colour picked.
    /// </summary>
    internal static HomeLight AsShown(HomeLight light, bool musicSync, IReadOnlyList<Color> cover) =>
        light.Rgb && musicSync && cover.Count > 0 ? light with { Color = cover[0] } : light;

    private HomeLight AsShown(HomeLight light) => AsShown(light, _musicSync == true, CoverColorsFile.Current);

    private void OnCoverColorsChanged()
    {
        if (_disposed || Page != ControlCenterPage.Pi || _home is not { } home || _musicSync != true) return;
        UpdateHomeFace(home);
        UpdateHome(home);
    }

    /// <summary>A light's colour as the notch shows it: its hue, but with little saturation and a set lightness.</summary>
    internal static Color Calm(Color c, double maxSaturation, double lightness)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h = (h * 60 + 360) % 360;
        }
        double l = (max + min) / 2;
        double s = d == 0 ? 0 : d / (1 - Math.Abs(2 * l - 1));
        s = Math.Min(s, maxSaturation);
        l = lightness;
        double chroma = (1 - Math.Abs(2 * l - 1)) * s, x = chroma * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - chroma / 2;
        (double r1, double g1, double b1) = h switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            < 300 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x)
        };
        return Color.FromRgb((byte)Math.Round((r1 + m) * 255), (byte)Math.Round((g1 + m) * 255), (byte)Math.Round((b1 + m) * 255));
    }

    private void UpdateHome(PiHome home)
    {
        if (_stripDrawer != null && home.Lights.FirstOrDefault(l => l.Rgb) is { } strip) _stripDrawer(AsShown(strip));
        if (_humidity != null)
        {
            double? humidity = home.Climate?.Humidity;
            _humidity.Text = humidity.HasValue ? Loc.Get("home.humidity", Math.Round(humidity.Value)) : "";
        }
        foreach (var plug in home.Plugs)
            if (_plugReadouts.TryGetValue(plug.Id, out var readout)) readout.Show(plug);
        if (home.Pi != null) _piReadout?.Show(home.Pi);
    }

    private async void SendHome(string action, string? id, JsonNode? value)
    {
        await PostHome(action, id, value);
        RefreshHomeAfterCommand();
    }

    private async Task PostHome(string action, string? id, JsonNode? value, Action<bool>? acknowledged = null)
    {
        if (_pi == null || _disposed) return;
        bool ok = await _pi.SendHomeAsync(action, id, value);
        if (_disposed) return;
        acknowledged?.Invoke(ok);
        if (ok) await Task.Delay(HomeSettleMs);
    }

    private void RefreshHomeAfterCommand()
    {
        if (_disposed) return;
        if (_piRefreshing) _homeRefreshQueued = true;
        else RefreshPi();
    }

    private FrameworkElement StripDrawer(HomeLight light, IReadOnlyList<HomeColor> colors)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        var dots = new WrapPanel { Margin = new Thickness(0, 5, 0, 2) };
        var swatches = new List<(Ellipse Dot, Color Color)>();
        foreach (var color in colors.Take(8))
        {
            var dot = new Ellipse
            {
                Width = 12, Height = 12, Fill = new SolidColorBrush(Calm(color.Color, .27, .66)),
                Margin = new Thickness(2, 2, 5, 2), StrokeThickness = 1, ToolTip = color.Name, Cursor = Cursors.Hand
            };
            swatches.Add((dot, color.Color));
            Squish(dot);
            dot.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                SendHome("light", light.Id, new JsonObject { ["rgb"] = new JsonArray(color.Color.R, color.Color.G, color.Color.B) });
            };
            dots.Children.Add(dot);
        }

        var colorButton = CompactPill(Say("Farbe", "Colour"), () => Select(false));
        var musicButton = CompactPill(Say("Musik", "Music"), () => Select(true));
        colorButton.Margin = new Thickness(0, 0, 6, 0);
        modes.Children.Add(colorButton);
        modes.Children.Add(musicButton);
        panel.Children.Add(CompactText(light.Name, 11, CompactLabel));
        modes.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(modes);
        panel.Children.Add(dots);
        _stripDrawer = state =>
        {
            bool music = _musicSync == true;
            panel.IsEnabled = state.On && state.Available;
            panel.Opacity = state.On && state.Available ? 1 : .4;
            colorButton.BorderThickness = musicButton.BorderThickness = new Thickness(1);
            colorButton.BorderBrush = music ? Brushes.Transparent : RoomLabel;
            musicButton.BorderBrush = music ? RoomLabel : Brushes.Transparent;
            musicButton.IsEnabled = _musicSync.HasValue;
            ToolTipService.SetShowOnDisabled(musicButton, true);
            musicButton.ToolTip = _musicSync == null ? Say("Musik-Sync ist auf diesem PC nicht eingerichtet", "Music sync is unavailable on this PC") : null;
            dots.Visibility = music ? Visibility.Collapsed : Visibility.Visible;
            foreach (var (dot, color) in swatches)
                dot.Stroke = state.Color == color ? CompactInk : RoomTrack;
        };
        return panel;

        async void Select(bool music)
        {
            if (_musicSync == music || _musicSync == null || !await GoveeSync.SetRunningAsync(music) || _disposed) return;
            _musicSync = music;
            if (_home is not { } home) return;
            UpdateHomeFace(home);
            UpdateHome(home);
        }
    }

    #region Climate

    private void BuildClimate(PiHome home)
    {
        if (home.Climate is { } climate)
        {
            var heading = CompactText(Loc.Get("home.heating"), 11, Muted);
            _humidity = CompactText("", 11, CompactLabel);
            var window = CompactText(climate.WindowOpen ? Say("Fenster offen", "Window open") : Say("Fenster zu", "Window closed"), 11, CompactLabel);
            var details = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Children = { heading, _humidity, window } };
            _homeClimateSection.Children.Add(details);
        }
        if (home.AirConditioner.Count > 0) _homeClimateSection.Children.Add(AirConditionerCard(home.AirConditioner));
    }

    private void StepHomeTarget(double step)
    {
        if (_home is not { Climate: { } climate } home || (_targetDraft ?? climate.Target) is not { } from) return;
        double low = Math.Max(5, climate.Min), high = Math.Min(30, climate.Max);
        if (high <= low) return;
        double next = Math.Clamp(Math.Round((from + step) * 2) / 2, low, high);
        if (from == next) return;
        _targetDraft = next;
        UpdateHomeFace(home);
        if (_targetSend == null)
        {
            _targetSend = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(TargetSendDelayMs) };
            _targetSend.Tick += SendTarget;
        }
        _targetSend.Stop();
        _targetSend.Start();
    }

    private async void SendTarget(object? sender, EventArgs e)
    {
        _targetSend?.Stop();
        if (_targetSending || _targetDraft is not { } value) return;
        _targetSending = true;
        try { await PostHome("temp", null, JsonValue.Create(value)); }
        finally
        {
            _targetSending = false;
            if (!_disposed && _targetSend?.IsEnabled != true)
            {
                if (_targetDraft == value) _targetDraft = null;
                else SendTarget(null, EventArgs.Empty);
            }
            RefreshHomeAfterCommand();
        }
    }


    /// <summary>The air conditioner's IR remote: one chip per button; it has no state to show, so a press just answers.</summary>
    private FrameworkElement AirConditionerCard(IReadOnlyList<HomeAction> buttons)
    {
        var title = CompactText(Loc.Get("home.ac"), 11, Muted);
        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var button in buttons)
        {
            bool power = button.Icon == "power";
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            if (power)
                label.Children.Add(new TextBlock { Text = Glyph(0xE7E8), FontFamily = SegoeSymbolFont, FontSize = 11, Foreground = _moodInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0) });
            label.Children.Add(CompactText(button.Name, 11.5, power ? _moodInk : VNotch.Services.UiPalette.PrimaryBrush));
            var chip = new Border
            {
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Background = power ? new SolidColorBrush(Alpha(_mood.Color, 0.22)) : new SolidColorBrush(((SolidColorBrush)ComboBg).Color),
                Cursor = Cursors.Hand,
                Child = label
            };
            string id = button.Id;
            chip.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Flash(chip);
                SendHome("ac", id, null);
            };
            Squish(chip);
            chips.Children.Add(chip);
        }
        var card = new Border { Background = Brushes.Transparent, Tag = "drawer-row" };
        card.Padding = new Thickness(0, 0, 0, 3);
        card.Child = new StackPanel { Children = { title, chips } };
        return card;

        void Flash(Border chip)
        {
            if (chip.Background is not SolidColorBrush { IsFrozen: false } fill) return;
            var rest = fill.Color;
            SetOrTint(fill, Alpha(_mood.Color, 0.55), 0);
            WeatherMotion.Tint(fill, rest, 220);
        }
    }

    #endregion

    #region Sockets and Pi

    private static string PlugText(HomePlug plug)
    {
        if (!plug.Available) return Loc.Get("control.noAdapter");
        var text = new StringBuilder(Loc.Get(plug.On ? "control.on" : "control.off"));
        if (plug.On && plug.Watts is { } watts) text.Append(" · ").Append(Math.Round(watts)).Append(" W");
        if (plug.TodayKwh is { } kwh && kwh > 0)
            text.Append(" · ").Append(Loc.Get("home.today", kwh.ToString("0.00", CultureInfo.CurrentCulture), (kwh * EuroPerKwh).ToString("0.00", CultureInfo.CurrentCulture)));
        return text.ToString();
    }

    private sealed class PlugReadout
    {
        private readonly TextBlock _text;
        private readonly RollingNumber _wattsRoll, _energyRoll;
        private HomePlug _plug;
        private double _watts, _energy;

        public PlugReadout(TextBlock text, HomePlug plug)
        {
            _text = text;
            _plug = plug;
            _watts = plug.Watts ?? 0;
            _energy = plug.TodayKwh ?? 0;
            _wattsRoll = new RollingNumber(value => { _watts = value; Paint(); }, text);
            _energyRoll = new RollingNumber(value => { _energy = value; Paint(); }, text);
            _wattsRoll.To(_watts, 0);
            _energyRoll.To(_energy, 0);
        }

        public void Show(HomePlug plug)
        {
            _plug = plug;
            _wattsRoll.To(plug.Watts ?? 0);
            _energyRoll.To(plug.TodayKwh ?? 0);
        }

        private void Paint()
        {
            _text.Text = PlugText(_plug with
        {
            Watts = _plug.Watts == null ? null : Math.Max(0, _watts),
            TodayKwh = _plug.TodayKwh == null ? null : Math.Max(0, _energy)
            });
            _text.ToolTip = _text.Text;
        }
    }

    private void BuildPlugs(PiHome home)
    {
        foreach (var plug in home.Plugs)
        {
            string id = plug.Id;
            var watts = CompactText(PlugText(plug), 10.5, CompactLabel);
            watts.MaxWidth = 190; watts.ToolTip = PlugText(plug);
            _plugStatus[id] = watts; _plugReadouts[id] = new PlugReadout(watts, plug);
            var action = new StackPanel { Orientation = Orientation.Horizontal };
            if (_guardArmed == id)
            {
                action.Children.Add(CompactText("Wirklich aus?", 10.5, CompactInk));
                action.Children.Add(CompactPill("Aus", () => { _guardArmed = null; SendHome("plug", id, JsonValue.Create(false)); RedrawHome(); }));
                action.Children.Add(CompactPill("Nein", () => { _guardArmed = null; RedrawHome(); }));
            }
            else
            {
                action.Children.Add(watts);
                if (plug.Guard) action.Children.Add(CompactGlyph(0xE72E));
                var toggle = Toggle(plug.On, on =>
                {
                    if (plug.Guard && !on) { _guardArmed = id; RedrawHome(); DisarmLater(id); }
                    else SendHome("plug", id, JsonValue.Create(on));
                });
                toggle.IsEnabled = plug.Available;
                toggle.Margin = new Thickness(7, 0, 0, 0); action.Children.Add(toggle);
            }
            var row = CompactLine(plug.Name == "PC" ? 0xE7F4 : plug.Name == "Pi" ? 0xE950 : 0xE945, plug.Name, action);
            if (!plug.Available) row.Opacity = .4;
            _homePlugSection.Children.Add(row);
        }
    }

    private void RedrawHome() { _homeKey = ""; BuildHome(); ContentHeightChanged?.Invoke(); }

    private void DisarmLater(string id)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = GuardWindow };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_guardArmed != id) return;
            _guardArmed = null;
            _homeKey = "";
            BuildHome();
        };
        timer.Start();
    }

    private static string PiText(HomePi pi)
    {
        var parts = new List<string>();
        if (pi.Temp is { } temp) parts.Add(Math.Round(temp) + "°");
        if (pi.UptimeSeconds is { } up) parts.Add(Loc.Get("home.piUp", FormatDuration(TimeSpan.FromSeconds(up))));
        if (pi.Issues.Count == 0) parts.Add(Loc.Get("home.piOk"));
        return string.Join(" · ", parts);
    }

    private sealed class PiReadout
    {
        private readonly TextBlock _text;
        private readonly RollingNumber _temperatureRoll, _uptimeRoll;
        private HomePi _pi;
        private double _temperature, _uptime;

        public PiReadout(TextBlock text, HomePi pi)
        {
            _text = text;
            _pi = pi;
            _temperature = pi.Temp ?? 0;
            _uptime = pi.UptimeSeconds ?? 0;
            _temperatureRoll = new RollingNumber(value => { _temperature = value; Paint(); }, text);
            _uptimeRoll = new RollingNumber(value => { _uptime = value; Paint(); }, text);
            _temperatureRoll.To(_temperature, 0);
            _uptimeRoll.To(_uptime, 0);
        }

        public void Show(HomePi pi)
        {
            _pi = pi;
            _temperatureRoll.To(pi.Temp ?? 0);
            _uptimeRoll.To(pi.UptimeSeconds ?? 0, 0);
        }

        private void Paint() => _text.Text = "Pi " + PiText(_pi with
        {
            Temp = _pi.Temp == null ? null : _temperature,
            UptimeSeconds = _pi.UptimeSeconds == null ? null : (long)Math.Max(0, _uptime)
        });
    }

    /// <summary>The Pi in one quiet line; checks that are not green get a row each, with their fix when there is one.</summary>
    private void BuildPiLine(PiHome home)
    {
        if (home.Pi is not { } pi) return;
        _piLine = CompactText("Pi " + PiText(pi), 11, CompactLabel);
        _piLine.TextWrapping = TextWrapping.Wrap;
        _piLine.Tag = "drawer-row";
        _piReadout = new PiReadout(_piLine, pi);
        _homePiSection.Children.Add(_piLine);
        foreach (var issue in pi.Issues)
        {
            FrameworkElement? fix = issue.FixId is { } id ? CompactPill(issue.FixLabel ?? Loc.Get("home.fix"), () => SendHome("fix", id, null)) : null;
            var row = CompactLine(0xE7BA, issue.Title, fix); row.ToolTip = issue.Detail;
            _homePiSection.Children.Add(row);
            var detail = CompactText(issue.Detail, 10.5, CompactLabel); detail.TextWrapping = TextWrapping.Wrap;
            _homePiSection.Children.Add(detail);
        }
    }

    #endregion

    #endregion
}
