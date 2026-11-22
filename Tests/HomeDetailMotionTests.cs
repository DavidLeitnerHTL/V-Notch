using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VNotch.Controls;
using VNotch.Services;
using VNotch.Services.ControlCenter;
using Xunit;
using static VNotch.Tests.HomeSchoolTabTests;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class HomeDetailMotionTests
{
    [Fact]
    public void DrawerNumbersRollAndReverseFromTheirLiveValue() => SharedStaTestRunner.RunAsync(async ct =>
    {
        using var scene = new Scene();
        scene.Update(240, 2.3, 64);
        var watts = scene.Watts;
        Assert.NotEqual(240, watts.Value);
        await WpfFrameWaiter.UntilAsync(() => watts.Value > 150, "socket wattage moves", ct);
        double value = watts.Value;
        double velocity = Motion.VelocityOf(watts, WeatherTween.ValueProperty);
        scene.Update(90, 1.2, 46);
        Assert.Equal(value, watts.Value, 5);
        Assert.Equal(Math.Sign(velocity), Math.Sign(Motion.VelocityOf(watts, WeatherTween.ValueProperty)));
        await TestWait.UntilStableAsync(() => !Motion.IsRunning(watts, WeatherTween.ValueProperty), "latest wattage settles", cancellationToken: ct);
        Assert.Equal(90, watts.Value);
        Assert.Contains("90 W", scene.Plug.Text);
        Assert.Matches("1[.,]20", scene.Plug.Text);
        Assert.Contains("46°", scene.Pi.Text);
    });

    [Fact]
    public void HiddenOrReducedDrawerReadoutsSetTheirFinalValuesWithoutClocks() => SharedStaTestRunner.RunAsync(async ct =>
    {
        using var scene = new Scene();
        AnimationConfig.SetReduceMotion(true);
        scene.Update(210, 1.8, 62);
        Assert.Contains("210 W", scene.Plug.Text);
        Assert.False(scene.Watts.HasAnimatedProperties);
        AnimationConfig.SetReduceMotion(false);
        scene.Update(80, 1.1, 42);
        await WpfFrameWaiter.UntilAsync(() => scene.Watts.Value < 210, "wattage roll starts", ct);
        scene.Root.Visibility = Visibility.Collapsed;
        scene.Update(120, 1.4, 51);
        Assert.Contains("120 W", scene.Plug.Text);
        Assert.Contains("51°", scene.Pi.Text);
        Assert.False(scene.Watts.HasAnimatedProperties);
    });

    private sealed class Scene : IDisposable
    {
        private readonly bool _reduced = AnimationConfig.ReduceMotion;
        private readonly string _language = Loc.CurrentLanguage;
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly bool? _clientAnimations = AnimationConfig.ClientAreaAnimationOverride;
        private readonly object _plugReadout, _piReadout;
        private readonly DetailMotionFrames _frames;
        private readonly ControlCenterView _view = new() { Page = ControlCenterPage.Pi };
        private readonly BackgroundWindow _window;
        private readonly HomePlug _socket = new("test-socket", "PC", true, true, false, 140, 1.5);
        private readonly HomePi _computer = new(55, 7200, []);
        internal Border Root { get; }
        internal TextBlock Plug { get; }
        internal TextBlock Pi { get; }
        internal WeatherTween Watts => (WeatherTween)Field(Field(_plugReadout, "_wattsRoll"), "_tween");

        internal Scene()
        {
            Loc.SetLanguage("en");
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            AnimationConfig.ClientAreaAnimationOverride = true;
            AnimationConfig.SetReduceMotion(false);
            var home = new PiHome([], [], null, [], [_socket], [], _computer);
            Invoke(_view, "BuildPlugs", home);
            Invoke(_view, "BuildPiLine", home);
            _plugReadout = ((IDictionary)Field(_view, "_plugReadouts"))[_socket.Id]!;
            _piReadout = Field(_view, "_piReadout");
            Plug = ((Dictionary<string, TextBlock>)Field(_view, "_plugStatus"))[_socket.Id];
            Pi = (TextBlock)Field(_view, "_piLine");
            var body = new StackPanel { Children = { (StackPanel)Field(_view, "_homePlugSection"), (StackPanel)Field(_view, "_homePiSection") } };
            Root = new Border { Width = 320, Height = 160, Background = Brushes.Black, Child = body };
            _window = new BackgroundWindow { Content = Root, Width = 320, Height = 160, Left = -20000, Top = -20000 };
            _window.Show();
            Root.UpdateLayout();
            _frames = new DetailMotionFrames(Root);
        }

        internal void Update(double watts, double energy, double temperature)
        {
            InvokeReadout(_plugReadout, _socket with { Watts = watts, TodayKwh = energy });
            InvokeReadout(_piReadout, _computer with { Temp = temperature, UptimeSeconds = 7260 });
        }

        private static void InvokeReadout(object target, object value) =>
            target.GetType().GetMethod("Show")!.Invoke(target, [value]);

        public void Dispose()
        {
            _frames.Dispose();
            _window.Close();
            _view.Dispose();
            Loc.SetLanguage(_language);
            CultureInfo.CurrentCulture = _culture;
            AnimationConfig.ClientAreaAnimationOverride = _clientAnimations;
            AnimationConfig.SetReduceMotion(_reduced);
        }
    }
}
