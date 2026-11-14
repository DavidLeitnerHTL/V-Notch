using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VNotch.Controls;
using VNotch.Services;
using VNotch.Services.ClaudeCode;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class ClaudeDetailMotionTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void LimitBarsFillAndRetargetAcrossRebuildsWithoutLosingPositionOrVelocity() => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool previous = AnimationConfig.ReduceMotion;
        AnimationConfig.SetReduceMotion(false);
        try
        {
            using var fixture = new Fixture(25);
            var bar = fixture.Bar;
            var grow = Field<ScaleTransform>(bar, "Grow");
            var number = Field<WeatherTween>(bar, "Number");
            Assert.True(grow.ScaleX < .25);
            await Settled(grow, number, ct);
            Assert.Equal(.25, grow.ScaleX, 8);
            fixture.SetUsage(85);
            await WpfFrameWaiter.UntilAsync(() => grow.ScaleX > .35, "limit advancing", ct);
            double position = grow.ScaleX, velocity = Motion.VelocityOf(grow, ScaleTransform.ScaleXProperty);
            double shown = number.Value;
            fixture.SetUsage(10);
            Assert.Same(bar, fixture.Bar);
            Assert.Equal(position, grow.ScaleX, 6);
            Assert.Equal(shown, number.Value, 6);
            Assert.InRange(Math.Abs(Motion.VelocityOf(grow, ScaleTransform.ScaleXProperty) - velocity), 0, .03);
            await Settled(grow, number, ct);
            Assert.Equal(.1, grow.ScaleX, 8);
            Assert.Equal(10, number.Value, 8);
            fixture.View.Visibility = Visibility.Collapsed;
            Assert.False(grow.HasAnimatedProperties);
            Assert.False(number.HasAnimatedProperties);
        }
        finally { AnimationConfig.SetReduceMotion(previous); }
    });

    [Fact]
    public void ReducedMotionSetsExactLimitsAndHidingStopsAnUnfinishedFill() => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool previous = AnimationConfig.ReduceMotion;
        AnimationConfig.SetReduceMotion(true);
        try
        {
            using var fixture = new Fixture(40);
            var grow = Field<ScaleTransform>(fixture.Bar, "Grow");
            var number = Field<WeatherTween>(fixture.Bar, "Number");
            fixture.SetUsage(80);
            Assert.Equal(.8, grow.ScaleX);
            Assert.Equal(80, number.Value);
            Assert.False(grow.HasAnimatedProperties);
            Assert.False(number.HasAnimatedProperties);
            AnimationConfig.SetReduceMotion(false);
            fixture.SetUsage(20);
            await WpfFrameWaiter.UntilAsync(() => grow.ScaleX < .7, "limit starts leaving", ct);
            fixture.View.Visibility = Visibility.Collapsed;
            Assert.False(grow.HasAnimatedProperties);
            Assert.False(number.HasAnimatedProperties);
            Assert.Equal(.2, grow.ScaleX);
            Assert.Equal(20, number.Value);
        }
        finally { AnimationConfig.SetReduceMotion(previous); }
    });

    [Fact]
    public void TaskShareCountsToItsLatestValueAndStopsWhenHidden() => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool previous = AnimationConfig.ReduceMotion;
        AnimationConfig.SetReduceMotion(false);
        try
        {
            using var fixture = new Fixture(30);
            var session = new ClaudeSession { Id = "fake" };
            var host = (FrameworkElement)Invoke(fixture.View, "TokenStats", session)!;
            fixture.Section.Children.Add(host);
            object row = Field<object>(fixture.View, "_tokenRow");
            void Show(double? share) => Invoke(fixture.View, "ShowTokens", row,
                new ClaudeSessionTokens(default, default, 1000, share), false);
            Show(12);
            Show(68);
            var number = Field<WeatherTween>(Field<object>(row, "ShareNumber"), "_tween");
            Assert.Equal(12, number.Value);
            await WpfFrameWaiter.UntilAsync(() => number.Value > 20, "task share counts", ct);
            double shown = number.Value;
            Show(25);
            Assert.Equal(shown, number.Value, 6);
            await WpfFrameWaiter.UntilAsync(() => !Motion.IsRunning(number, WeatherTween.ValueProperty), "task share settles", ct);
            Assert.Equal(ControlCenterView.FormatShare(25), Field<TextBlock>(row, "Share").Text);
            Show(null);
            await WpfFrameWaiter.UntilAsync(() => Field<TextBlock>(row, "Share").Text == "–", "unknown share fades in", ct);
            Assert.Equal("–", Field<TextBlock>(row, "Share").Text);
            Assert.False(number.HasAnimatedProperties);
            Show(75);
            fixture.View.Visibility = Visibility.Collapsed;
            Assert.False(number.HasAnimatedProperties);
        }
        finally { AnimationConfig.SetReduceMotion(previous); }
    });

    internal static async Task Settled(ScaleTransform grow, WeatherTween number, CancellationToken ct) =>
        await WpfFrameWaiter.UntilAsync(() => !Motion.IsRunning(grow, ScaleTransform.ScaleXProperty) &&
            !Motion.IsRunning(number, WeatherTween.ValueProperty), "Claude limit settles", ct);

    internal static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    internal static object? Invoke(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    internal sealed class Fixture : IDisposable
    {
        internal ControlCenterView View { get; } = new() { Page = ControlCenterPage.Claude };
        internal Window Window { get; }
        private readonly ClaudeUsageClient _usage = new(new FakeHandler(), Path.Combine(Path.GetTempPath(), "vnotch-no-claude-profile"));
        internal StackPanel Section => Field<StackPanel>(View, "_claudeSection");
        internal object Bar => Field<IDictionary>(View, "_limitVisuals")["5h"]!;

        internal Fixture(double percent)
        {
            Set("_claude", new ClaudeSessionTracker());
            Set("_claudeInstalled", true);
            Set("_usage", _usage);
            Set("_codexReading", true);
            Set("_tokenReading", true);
            _usage.Latest = Limits(percent);
            Window = new Window
            {
                Left = -20000, Top = -20000, Width = 620, Height = 410, WindowStyle = WindowStyle.None,
                ShowActivated = false, ShowInTaskbar = false, Background = new SolidColorBrush(Color.FromRgb(23, 23, 25)), Content = View
            };
            Window.Show();
            Window.UpdateLayout();
        }

        internal void SetUsage(double percent)
        {
            _usage.Latest = Limits(percent);
            Invoke(View, "BuildClaudeSection");
            Window.UpdateLayout();
        }

        private static ClaudeLimits Limits(double percent) => new(new ClaudeLimitWindow(percent, null), new ClaudeLimitWindow(33, null));
        private void Set(string name, object value) => View.GetType().GetField(name, Fields)!.SetValue(View, value);
        public void Dispose() { View.Dispose(); Window.Close(); _usage.Dispose(); }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
    }
}
