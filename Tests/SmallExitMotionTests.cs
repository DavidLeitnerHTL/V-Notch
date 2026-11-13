using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class SmallExitMotionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void IdleGlanceFadesOut() => Run(async (window, ct) =>
    {
        SeedVisible(window, "ShowIdleGlance");
        Invoke(window, "ShowIdleGlance", false);
        Assert.True(window.IdleGlance.Opacity > 0);
        Assert.Equal(Visibility.Visible, window.IdleGlance.Visibility);
        await Task.Delay(220, ct);
        Assert.Equal(Visibility.Collapsed, window.IdleGlance.Visibility);
        Assert.Equal(-4, window.IdleGlanceShift.Y, 3);
    });

    [Fact]
    public void CallGlyphShrinksOut() => Run(async (window, ct) =>
    {
        SeedVisible(window, "SetMicMutedGlyph");
        Invoke(window, "SetMicMutedGlyph", false);
        Assert.True(window.MicMutedGlyph.Opacity > 0);
        Assert.Equal(Visibility.Visible, window.MicMutedGlyph.Visibility);
        await Task.Delay(240, ct);
        Assert.Equal(Visibility.Collapsed, window.MicMutedGlyph.Visibility);
        Assert.Equal(0.6, window.MicMutedGlyphScale.ScaleX, 3);
        Assert.Equal(0.6, window.MicMutedGlyphScale.ScaleY, 3);
    });

    [Fact]
    public void CrossFadeTextSwapsMidway() => Run(async (window, ct) =>
    {
        var target = window.ClaudeNotificationStatus;
        target.Text = "Before";
        Invoke(window, "CrossFadeText", target, "After", 120);
        Assert.Equal("Before", target.Text);
        await Task.Delay(80, ct);
        Assert.Equal("After", target.Text);
        await Task.Delay(80, ct);
        Assert.Equal(1, target.Opacity, 3);
    });

    [Theory]
    [InlineData("ShowIdleGlance")]
    [InlineData("SetMicMutedGlyph")]
    public void ShowingAgainCancelsTheExit(string method) => Run(async (window, ct) =>
    {
        SeedVisible(window, method);
        Invoke(window, method, false);
        await Task.Delay(60, ct);
        Invoke(window, method, true);
        await Task.Delay(800, ct);
        FrameworkElement target = method == "ShowIdleGlance" ? window.IdleGlance : window.MicMutedGlyph;
        Assert.Equal(Visibility.Visible, target.Visibility);
        Assert.Equal(1, target.Opacity, 3);
    });

    [Fact]
    public void RepeatedTextRequestsDoNotRestartOrOverwriteTheLatestSwap() => Run(async (window, ct) =>
    {
        var target = window.ClaudeNotificationStatus;
        target.Text = "Before";
        Invoke(window, "CrossFadeText", target, "After", 120);
        await Task.Delay(40, ct);
        Invoke(window, "CrossFadeText", target, "After", 120);
        await Task.Delay(40, ct);
        Assert.Equal("After", target.Text);
        Invoke(window, "CrossFadeText", target, "Final", 120);
        await Task.Delay(160, ct);
        Assert.Equal("Final", target.Text);
        Assert.Equal(1, target.Opacity, 3);
    });

    [Fact]
    public void ReducedMotionChangesStateImmediatelyAndCancelsPendingSwaps() => Run(async (window, ct) =>
    {
        SeedVisible(window, "ShowIdleGlance");
        SeedVisible(window, "SetMicMutedGlyph");
        var target = window.ClaudeNotificationStatus;
        Invoke(window, "CrossFadeText", target, "Stale", 120);
        AnimationConfig.SetReduceMotion(true);
        Invoke(window, "ShowIdleGlance", false);
        Invoke(window, "SetMicMutedGlyph", false);
        Invoke(window, "CrossFadeText", target, "Now", 120);
        Assert.Equal(Visibility.Collapsed, window.IdleGlance.Visibility);
        Assert.Equal(Visibility.Collapsed, window.MicMutedGlyph.Visibility);
        Assert.Equal("Now", target.Text);
        Assert.Equal(1, target.Opacity);
        await Task.Delay(160, ct);
        Assert.Equal("Now", target.Text);
    });

    private static void SeedVisible(MainWindow window, string method)
    {
        AnimationConfig.SetReduceMotion(true);
        Invoke(window, method, false);
        Invoke(window, method, true);
        AnimationConfig.SetReduceMotion(false);
    }

    private static void Run(Func<MainWindow, CancellationToken, Task> test) => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool reduced = AnimationConfig.ReduceMotion;
        await using var fixture = new GreetingAcceptanceTests.MainWindowFixture("en", false,
            settings => { settings.EnableLocalOnlyMode = true; settings.DisableMouseLeaveAutoClose = true; },
            services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
        var renderHost = new Window { Width = 1, Height = 1, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            var window = fixture.Window;
            // Exercise the visuals without starting desktop watchers or notification access.
            await window.CleanupAsync();
            renderHost.Show();
            AnimationConfig.SetReduceMotion(false);
            await test(window, ct);
        }
        finally
        {
            renderHost.Close();
            AnimationConfig.SetReduceMotion(reduced);
        }
    });

    private static void Invoke(MainWindow window, string name, params object[] arguments)
    {
        var method = typeof(MainWindow).GetMethod(name, Private);
        Assert.NotNull(method);
        method.Invoke(window, arguments);
    }
}
