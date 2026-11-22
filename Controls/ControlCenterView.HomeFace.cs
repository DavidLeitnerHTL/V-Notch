using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VNotch.Services;
using VNotch.Services.ControlCenter;

namespace VNotch.Controls;

public partial class ControlCenterView
{
    private static readonly Color RoomSand = Color.FromRgb(0xD2, 0xAE, 0x7C);
    private static readonly Color RoomMist = Color.FromRgb(0x7F, 0xA6, 0xCC);
    private static readonly Color RoomLilac = Color.FromRgb(0xA1, 0x9B, 0xD2);
    private static readonly Brush RoomLabel = Frozen("#8CFFFFFF");
    private static Brush RoomTrack => CompactTrack;

    private StackPanel? _homeFace;
    private string _homeFaceKey = "";
    private Canvas? _homeLightLayer;
    private readonly List<Action<PiHome>> _homeFaceUpdates = new();
    private readonly List<Action> _homeLightPlacements = new();
    private readonly HashSet<string> _homeFaceValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double?> _homeFaceLastSeen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _homeSwitchDrafts = new(StringComparer.Ordinal);
    private bool _placeHomeLightsQueued;

    private void UpdateHomeFace(PiHome home)
    {
        var plugs = home.Plugs.Where(p => !p.Guard).Take(3).ToArray();
        string key = string.Join("|", home.Lights.Select(l => $"{l.Id}:{l.Name}:{l.Rgb}")) + "/" +
            string.Join("|", plugs.Select(p => $"{p.Id}:{p.Name}")) + "/" +
            string.Join("|", home.Scenes.Select(s => $"{s.Id}:{s.Name}:{s.Icon}")) +
            $"/{home.Climate != null}/{Loc.CurrentLanguage}";
        if (_homeFace == null || _homeFaceKey != key)
        {
            var previous = _homeFaceValues.ToArray();
            ClearHomeFace();
            BuildHomeFace(home, plugs);
            foreach (string removed in previous)
            {
                if (_homeFaceValues.Contains(removed)) continue;
                Seen.Forget(removed);
                _homeFaceLastSeen.Remove(removed);
            }
            _homeFaceKey = key;
        }
        foreach (var update in _homeFaceUpdates) update(home);
    }

    private void ClearHomeFace()
    {
        foreach (var placeholder in _homeLightSection.Children.OfType<SkeletonPlaceholder>()) placeholder.Dispose();
        _homeLightSection.Children.Clear();
        _homeFace = null;
        _homeFaceUpdates.Clear();
        _homeFaceValues.Clear();
        _homeLightPlacements.Clear();
        _homeLightLayer?.Children.Clear();
    }

    private void BuildHomeFace(PiHome home, HomePlug[] plugs)
    {
        var face = _homeFace = new StackPanel { Name = "HomeFace", Margin = new Thickness(0, 5, 0, 0) };
        var row = new Grid();
        var lamps = HomeDeviceGroup(home.Lights.Count, 72);
        var sockets = HomeDeviceGroup(plugs.Length, 76);
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, home.Lights.Count * 72), GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1, plugs.Length * 76), GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (home.Lights.Count > 0 && plugs.Length > 0) sockets.Margin = new Thickness(18, 0, 0, 0);
        Grid.SetColumn(sockets, 1);
        row.Children.Add(lamps);
        row.Children.Add(sockets);
        face.Children.Add(row);
        if (home.Climate != null)
        {
            var thermostat = BuildHomeThermostat();
            Grid.SetColumn(thermostat, 2);
            row.Children.Add(thermostat);
        }

        var night = AddHomeLight(RoomMist, .46);
        PlaceHomeLight(night, () => new Point(Math.Min(132, _homeLightLayer!.ActualWidth / 3), -64), 460, 270);
        var showNight = TrackHomeValue(face, "night", value => night.Opacity = value ?? 0);
        _homeFaceUpdates.Add(data => showNight(data.Lights.Any(l => l.Available && HomeSwitchShown(l.Id, l.On)) ? 0 : 1));
        for (int i = 0; i < home.Lights.Count; i++)
        {
            HomeLight light = home.Lights[i];
            lamps.Children.Add(light.Rgb ? BuildHomeStrip(light) : BuildHomeLamp(light, i == 0));
        }
        foreach (var plug in plugs) sockets.Children.Add(BuildHomeSocket(plug));

        if (home.Scenes.Count > 0)
        {
            var scenes = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
            foreach (var scene in home.Scenes) scenes.Children.Add(BuildHomeScene(scene));
            face.Children.Add(scenes);
        }
        face.SizeChanged += (_, _) => QueueHomeLightPlacement();
        face.IsVisibleChanged += (_, _) => { if (face.IsVisible) QueueHomeLightPlacement(); };
        _homeLightSection.Children.Add(face);
        QueueHomeLightPlacement();
    }

    private static UniformGrid HomeDeviceGroup(int count, double width) => new()
    {
        Columns = Math.Max(1, count), Rows = 1, MaxWidth = Math.Max(1, count * width),
        VerticalAlignment = VerticalAlignment.Top
    };

    private FrameworkElement BuildHomeLamp(HomeLight light, bool overhead)
    {
        var restingInk = ((SolidColorBrush)RoomLabel).Color;
        var restingFill = ((SolidColorBrush)RoomTrack).Color;
        var ink = new SolidColorBrush(restingInk);
        var fill = new SolidColorBrush(restingFill);
        var icon = CompactGlyph(0xE82F, ink);
        icon.Width = 28;
        icon.FontSize = 25;
        var glow = new Ellipse { Width = 88, Height = 88, Opacity = 0, Fill = HomeLightBrush(RoomSand, .56) };
        var disc = HomeDisc(60, icon);
        disc.Background = fill;
        var halo = new Canvas { Width = 0, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false, Children = { glow } };
        Canvas.SetLeft(glow, -44);
        Canvas.SetTop(glow, 14);
        var body = new Grid { Height = 88, Children = { halo, disc } };
        var lamp = HomeDevice(light.Name, body);
        string id = light.Id;
        var room = AddHomeLight(RoomSand, overhead ? .62 : .50);
        PlaceHomeLight(room, () =>
        {
            Point center = disc.TranslatePoint(new Point(30, 30), _homeLightLayer);
            return overhead ? new Point(center.X + 31, center.Y - 95) : new Point(center.X - 72, center.Y - 15);
        }, overhead ? 370 : 310, overhead ? 285 : 195);
        var grow = new ScaleTransform(.7, .7);
        room.RenderTransformOrigin = new Point(.5, .5);
        room.RenderTransform = grow;
        var show = TrackHomeValue(lamp, "light:" + id, value =>
        {
            double level = Math.Clamp(value ?? 0, 0, 1);
            glow.Opacity = room.Opacity = level;
            fill.Color = Mix(restingFill, RoomSand, level);
            ink.Color = Mix(restingInk, Color.FromRgb(0x30, 0x26, 0x19), level);
            grow.ScaleX = grow.ScaleY = .7 + .3 * level;
        });
        _homeFaceUpdates.Add(home =>
        {
            var state = home.Lights.First(l => l.Id == id);
            UpdateHomeSwitch(lamp, state.Available);
            show(state.Available && HomeSwitchShown(id, state.On) ? 1 : 0);
        });
        HookHomeSwitch(lamp, "light", id, () => _home?.Lights.FirstOrDefault(l => l.Id == id)?.On);
        return lamp;
    }

    private FrameworkElement BuildHomeStrip(HomeLight light)
    {
        const double width = 48, height = 88;
        var fillInk = new SolidColorBrush(RoomLilac);
        var fill = new Border { Background = fillInk, Height = 0, VerticalAlignment = VerticalAlignment.Bottom };
        var hint = new Border
        {
            Background = fillInk, Height = 3, Width = 24, CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5), Opacity = .45
        };
        var glyphClip = new RectangleGeometry();
        var dark = new Grid { Width = width, Height = height, Clip = glyphClip, Children = { StripGlyph(Frozen("#232124")) } };
        var capsule = new Grid
        {
            Name = "HomeGoveeCapsule", Width = width, Height = height, HorizontalAlignment = HorizontalAlignment.Center,
            Background = RoomTrack, Clip = new RectangleGeometry(new Rect(0, 0, width, height), 24, 24),
            Children = { StripGlyph(CompactInk), fill, dark, hint }
        };
        var strip = HomeDevice(light.Name, capsule);
        var label = (TextBlock)((StackPanel)strip.Child).Children[1];
        var glowBrush = HomeLightBrush(RoomLilac, .77);
        var glow = AddHomeLight(glowBrush);
        PlaceHomeLight(glow, () => new Point(_homeLightLayer!.ActualWidth / 2, _homeLightLayer.ActualHeight + 42), 820, 208);
        string id = light.Id;
        Color? lastColor = null;
        var show = TrackHomeValue(strip, "light:" + id + ":level", value =>
        {
            double level = Math.Clamp(value ?? 0, 0, 1);
            glow.Opacity = level;
            fill.Height = height * level;
            glyphClip.Rect = new Rect(0, height - fill.Height, width, fill.Height);
        }, smooth: true);
        _homeFaceUpdates.Add(home =>
        {
            var state = home.Lights.First(l => l.Id == id);
            bool on = state.Available && HomeSwitchShown(id, state.On);
            Color? color = on ? AsShown(state).Color : lastColor ?? state.Color;
            if (color is { } rgb && lastColor != rgb)
            {
                lastColor = rgb;
                fillInk.Color = Calm(rgb, .27, .66);
                PaintHomeLight(glowBrush, fillInk.Color, .77);
            }
            hint.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            int? percent = state.Brightness is { } brightness ? Math.Clamp(brightness, 0, 100) : null;
            label.Text = on && percent is { } value ? $"Govee {value} %" : "Govee";
            UpdateHomeSwitch(strip, state.Available);
            if (state.Available)
                strip.ToolTip = light.Name + " · " + Say("Klicken zum Umschalten", "Click to toggle");
            show(on && percent is { } level ? level / 100.0 : 0);
        });
        HookHomeSwitch(strip, "light", id, () => _home?.Lights.FirstOrDefault(l => l.Id == id)?.On);
        return strip;

        TextBlock StripGlyph(Brush ink)
        {
            var glyph = CompactGlyph(0xE790, ink);
            glyph.FontSize = 20;
            glyph.Width = 24;
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Bottom;
            glyph.Margin = new Thickness(0, 0, 0, 12);
            return glyph;
        }
    }

    private FrameworkElement BuildHomeSocket(HomePlug plug)
    {
        string id = plug.Id;
        bool mic = plug.Name.Contains("Mikrofon", StringComparison.OrdinalIgnoreCase);
        var icon = HomeOutline(mic ? "M9 6 A3 3 0 0 1 15 6 V11 A3 3 0 0 1 9 11 Z M5.5 11 A6.5 6.5 0 0 0 18.5 11 M12 17.5 V21"
            : "M9 2.5 V7 M15 2.5 V7 M6.5 7 H17.5 V10.5 A5.5 5.5 0 0 1 6.5 10.5 Z M12 16 V21.5", 16);
        var disc = HomeDisc(36, icon);
        disc.Margin = new Thickness(0, 0, 0, 12);
        var dot = new Ellipse
        {
            Width = 4, Height = 4, Fill = CompactInk, Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var socket = HomeDevice(plug.Name, new Grid { Height = 88, Children = { disc, dot } });
        var show = TrackHomeValue(socket, "plug:" + id, value => dot.Opacity = value ?? 0);
        _homeFaceUpdates.Add(home =>
        {
            var state = home.Plugs.First(p => p.Id == id);
            bool on = state.Available && HomeSwitchShown(id, state.On);
            UpdateHomeSwitch(socket, state.Available);
            icon.Stroke = on ? CompactInk : RoomLabel;
            show(on ? 1 : 0);
        });
        HookHomeSwitch(socket, "plug", id, () => _home?.Plugs.FirstOrDefault(p => p.Id == id && !p.Guard)?.On);
        return socket;
    }

    private static Border HomeDisc(double size, UIElement child) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = RoomTrack, Child = child,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom
    };

    private Border HomeDevice(string name, UIElement body)
    {
        var label = CompactText(name, 11, RoomLabel);
        label.TextAlignment = TextAlignment.Center;
        label.TextWrapping = TextWrapping.Wrap;
        label.Margin = new Thickness(2, 7, 2, 0);
        var device = new Border
        {
            Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = name, VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel { Children = { body, label } }
        };
        AutomationProperties.SetName(device, name);
        Squish(device, () => device.IsEnabled);
        return device;
    }

    private static void UpdateHomeSwitch(Border device, bool available)
    {
        device.IsEnabled = available;
        device.Opacity = available ? 1 : .4;
        string name = AutomationProperties.GetName(device);
        device.ToolTip = available ? name : $"{name} · {Say("Offline", "Offline")}";
        ToolTipService.SetShowOnDisabled(device, true);
    }

    private bool HomeSwitchShown(string id, bool reported) => _homeSwitchDrafts.GetValueOrDefault(id, reported);

    private void HookHomeSwitch(Border button, string action, string id, Func<bool?> on)
    {
        button.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            if (!button.IsEnabled || on() is not { } current || !_homeSwitchDrafts.TryAdd(id, !current)) return;
            if (_home is { } home) UpdateHomeFace(home);
            try { await PostHome(action, id, JsonValue.Create(!current)); }
            finally
            {
                _homeSwitchDrafts.Remove(id);
                RefreshHomeAfterCommand();
            }
        };
    }

    private FrameworkElement BuildHomeThermostat()
    {
        var ring = new Grid { Width = 76, Height = 76, HorizontalAlignment = HorizontalAlignment.Center };
        var track = new Ellipse { Width = 68, Height = 68, StrokeThickness = 4, Stroke = RoomTrack };
        var arc = new Path { StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var marker = new Ellipse { Width = 7, Height = 7, Fill = CompactInk };
        var drawing = new Canvas { IsHitTestVisible = false, Children = { arc, marker } };
        var current = CompactText("", 20, CompactInk);
        current.FontWeight = FontWeights.SemiBold;
        current.HorizontalAlignment = HorizontalAlignment.Center;
        ring.Children.Add(track);
        ring.Children.Add(drawing);
        ring.Children.Add(current);

        var target = CompactText("", 12, CompactInk);
        target.FontWeight = FontWeights.SemiBold;
        var targetLabel = CompactText(Say("Ziel", "Target"), 11, RoomLabel);
        target.TextAlignment = targetLabel.TextAlignment = TextAlignment.Center;
        var targetReadout = new StackPanel { MinWidth = 52, Children = { target, targetLabel } };
        var minus = StepButton("−", -.5);
        var plus = StepButton("+", .5);
        var stepper = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0), Children = { minus, targetReadout, plus }
        };
        var heat = CompactText("", 11, RoomLabel);
        heat.Height = 15;
        heat.TextAlignment = TextAlignment.Center;
        heat.Margin = new Thickness(0, 3, 0, 0);
        var thermostat = new StackPanel { Name = "HomeThermostat", Width = 120, Children = { ring, stepper, heat } };
        double min = 5, max = 30;
        double Fraction(double value) => Math.Clamp((value - min) / (max - min), 0, 1);
        var showCurrent = TrackHomeValue(thermostat, "temperature:room", value =>
        {
            current.Text = Temperature(value);
            double fraction = value is { } degrees ? Fraction(degrees) : 0;
            arc.Data = fraction switch
            {
                <= 0 => null,
                >= 1 => new EllipseGeometry(new Point(38, 38), 32, 32),
                _ => new PathGeometry(new[] { new PathFigure(new Point(38, 6),
                    new[] { new ArcSegment(RingPoint(fraction), new Size(32, 32), 0, fraction > .5, SweepDirection.Clockwise, true) }, false) })
            };
        }, smooth: true);
        var showTarget = TrackHomeValue(thermostat, "temperature:target", value =>
        {
            target.Text = Temperature(value);
            marker.Visibility = value.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (value is not { } degrees) return;
            var point = RingPoint(Fraction(degrees));
            Canvas.SetLeft(marker, point.X - marker.Width / 2);
            Canvas.SetTop(marker, point.Y - marker.Height / 2);
        }, smooth: true);
        _homeFaceUpdates.Add(home =>
        {
            var climate = home.Climate!;
            double low = Math.Max(5, climate.Min), high = Math.Min(30, climate.Max);
            bool validRange = high > low;
            (min, max) = validRange ? (low, high) : (5, 30);
            double? setpoint = _targetDraft ?? climate.Target;
            minus.IsEnabled = validRange && setpoint > min;
            plus.IsEnabled = validRange && setpoint < max;
            minus.Opacity = minus.IsEnabled ? 1 : .4;
            plus.Opacity = plus.IsEnabled ? 1 : .4;
            showCurrent(climate.Current);
            showTarget(setpoint);
            heat.Text = climate.IsHeating ? Say("heizt", "Heating") : "";
            arc.Stroke = climate.IsHeating ? new SolidColorBrush(RoomSand) : RoomLabel;
        });
        return thermostat;

        Border StepButton(string sign, double step)
        {
            var label = CompactText(sign, 15, CompactInk);
            label.TextAlignment = TextAlignment.Center;
            var button = HomeDisc(24, label);
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Cursor = Cursors.Hand;
            button.ToolTip = Say($"Ziel {sign}0,5°", $"Target {sign}0.5°");
            AutomationProperties.SetName(button, (string)button.ToolTip);
            Pressable(button, () => RoomTrack, CardHover);
            button.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (button.IsEnabled) StepHomeTarget(step);
            };
            return button;
        }

        static string Temperature(double? value) => value is { } degrees ? degrees.ToString("0.0", CultureInfo.CurrentCulture) + "°" : "–";
        static Point RingPoint(double fraction)
        {
            double radians = fraction * 2 * Math.PI;
            return new Point(38 + 32 * Math.Sin(radians), 38 - 32 * Math.Cos(radians));
        }
    }

    private Border BuildHomeScene(HomeAction scene)
    {
        string outline = scene.Icon switch
        {
            "moon" => "M19.5 14.5 A8 8 0 0 1 9.5 4.5 A8 8 0 1 0 19.5 14.5 Z",
            "game" => "M7 8 H17 A4 4 0 0 1 21 12.5 L20.4 15.7 A2.4 2.4 0 0 1 16.2 16.7 L14.8 15 H9.2 L7.8 16.7 A2.4 2.4 0 0 1 3.6 15.7 L3 12.5 A4 4 0 0 1 7 8 Z M7.5 10.5 V13.5 M6 12 H9 M16.5 12 H17",
            "book" => "M4 5.5 A2.5 2.5 0 0 1 6.5 3 H20 V18 H6.5 A2.5 2.5 0 0 0 4 20.5 Z M4 20.5 A2.5 2.5 0 0 0 6.5 23 H20",
            "wind" => "M3 9 H14 A3 3 0 1 0 11 6 M3 15 H18 A3 3 0 1 1 15 18 M3 12 H10",
            _ => "M12 3 V7 M12 17 V21 M3 12 H7 M17 12 H21"
        };
        var icon = HomeOutline(outline, 14);
        var original = icon.Data;
        var check = Geometry.Parse("M5 12.5 L9.5 17 L19 7.5");
        icon.Margin = new Thickness(0, 0, 7, 0);
        var name = CompactText(scene.Name, 12, CompactInk);
        var pill = new Border
        {
            CornerRadius = new CornerRadius(15), Padding = new Thickness(9, 6, 13, 6), Margin = new Thickness(0, 0, 8, 2),
            Background = RoomTrack, Cursor = Cursors.Hand,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, name } }
        };
        AutomationProperties.SetName(pill, scene.Name);
        Pressable(pill, () => RoomTrack, CardHover);
        bool pending = false;
        int visibility = 0;
        pill.IsVisibleChanged += (_, _) =>
        {
            if (pill.IsVisible) return;
            visibility++;
            icon.Data = original;
        };
        pill.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            if (pending) return;
            pending = true;
            int visit = visibility;
            try
            {
                await PostHome("scene", scene.Id, null, ok =>
                {
                    if (ok && pill.IsVisible && visit == visibility) icon.Data = check;
                });
            }
            finally
            {
                icon.Data = original;
                pending = false;
                RefreshHomeAfterCommand();
            }
        };
        return pill;
    }

    private static Path HomeOutline(string geometry, double size) => new()
    {
        Data = Geometry.Parse(geometry), Width = size, Height = size, Stretch = Stretch.Uniform, Stroke = CompactInk,
        StrokeThickness = 1.35, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };

    private Action<double?> TrackHomeValue(FrameworkElement owner, string key, Action<double?> paint, bool smooth = false)
    {
        key = "home.face." + key;
        _homeFaceValues.Add(key);
        double? target = null;
        bool initialized = false;
        var tween = new WeatherTween { Step = value => paint(value) };
        owner.IsVisibleChanged += (_, _) =>
        {
            if (!owner.IsVisible && target is { } rest) Motion.Stop(tween, WeatherTween.ValueProperty, rest);
        };
        return value =>
        {
            bool changed = value is { } number ? Seen.Changed(key, number, "0.###") : Seen.Changed(key, "");
            if (changed && !AnimationConfig.ReduceMotion && _homeFaceLastSeen.GetValueOrDefault(key) is { } before && value is { } next)
            {
                double? from = !initialized || target == value ? before : null;
                Motion.To(tween, WeatherTween.ValueProperty, next, smooth || next > before ? Motion.Shift : Motion.Exit, from: from);
            }
            else if (!initialized || target != value || !IsVisible || AnimationConfig.ReduceMotion)
            {
                Motion.Stop(tween, WeatherTween.ValueProperty, value ?? 0);
                paint(value);
            }
            initialized = true;
            target = value;
            if (IsVisible) _homeFaceLastSeen[key] = value;
        };
    }

    private Ellipse AddHomeLight(Color color, double strength) => AddHomeLight(HomeLightBrush(color, strength));

    private Ellipse AddHomeLight(Brush brush) => new() { Fill = brush, Opacity = 0 };

    private static RadialGradientBrush HomeLightBrush(Color color, double strength)
    {
        var brush = new RadialGradientBrush { GradientStops = { new GradientStop(), new GradientStop(Colors.Transparent, .48), new GradientStop(Colors.Transparent, 1) } };
        PaintHomeLight(brush, color, strength);
        return brush;
    }

    private static void PaintHomeLight(RadialGradientBrush brush, Color color, double strength)
    {
        brush.GradientStops[0].Color = Alpha(color, strength);
        brush.GradientStops[1].Color = Alpha(color, strength * .39);
        brush.GradientStops[2].Color = Alpha(color, 0);
    }

    private void PlaceHomeLight(Ellipse light, Func<Point> locate, double width, double height)
    {
        light.Width = width;
        light.Height = height;
        _homeLightPlacements.Add(() =>
        {
            var center = locate();
            Canvas.SetLeft(light, center.X - width / 2);
            Canvas.SetTop(light, center.Y - height / 2);
            if (light.Parent == null) _homeLightLayer!.Children.Add(light);
        });
    }

    private void QueueHomeLightPlacement()
    {
        if (_placeHomeLightsQueued) return;
        _placeHomeLightsQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _placeHomeLightsQueued = false;
            if (_disposed || !IsVisible || _backdrop is not { IsVisible: true } backdrop) return;
            if (_homeLightLayer == null)
            {
                _homeLightLayer = backdrop.UseRoomLights();
                _homeLightLayer.SizeChanged += (_, _) => QueueHomeLightPlacement();
            }
            foreach (var place in _homeLightPlacements) place();
        });
    }
}
