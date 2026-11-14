using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Models;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class OrbPhoneMotionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void BudReversalKeepsPositionAndMomentum() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = Create();
        var window = fixture.Window;
        window.Left = -20000;
        window.Show();
        await WpfFrameWaiter.UntilAsync(() => window.CollapsedContent.IsVisible, "closed notch", ct);
        Set(window, "_bubbleShown", true);
        Invoke(window, "BudBubble");
        await Task.Delay(100, ct);
        double position = window.BubbleSlide.X;
        double velocity = Motion.VelocityOf(window.BubbleSlide, TranslateTransform.XProperty);
        Invoke(window, "RetractBubble", false);
        Assert.InRange(Math.Abs(window.BubbleSlide.X - position), 0, 0.5);
        Assert.InRange(Math.Abs(Motion.VelocityOf(window.BubbleSlide, TranslateTransform.XProperty) - velocity), 0, 25);
        await Task.Delay(80, ct);
        position = window.BubbleSlide.X;
        Invoke(window, "BudBubble");
        Assert.InRange(Math.Abs(window.BubbleSlide.X - position), 0, 0.5);
        await Task.Delay(800, ct);
        Assert.Equal(Visibility.Visible, window.ActivityBubble.Visibility);
        Assert.InRange(Math.Abs(window.BubbleSlide.X), 0, 0.1);
    });

    [Fact]
    public void PhoneRetargetKeepsAllRectangleVelocitiesAndStopsWhenHidden() => SharedStaTestRunner.RunAsync(async ct =>
    {
        var window = new PhoneMirrorWindow(() => null);
        try
        {
            window.Left = -20000;
            window.Top = -20000;
            window.Width = 80;
            window.Height = 20;
            window.Show();
            var start = new Int32Rect(-20000, -20000, 80, 20);
            var target = new Int32Rect(-20050, -19980, 180, 360);
            Invoke(window, "Spring", start, target, Motion.Pop, null);
            await Task.Delay(100, ct);
            var previous = Get<SpringDoubleAnimation[]>(window, "_rectangleSprings");
            double elapsed = Get<System.Diagnostics.Stopwatch>(window, "_springClock").Elapsed.TotalSeconds;
            double[] velocities = previous.Select(x => x.VelocityAt(elapsed)).ToArray();
            Invoke(window, "Spring", start, start, Motion.PopBack, null);
            var next = Get<SpringDoubleAnimation[]>(window, "_rectangleSprings");
            for (int i = 0; i < 4; i++)
                Assert.InRange(Math.Abs(next[i].VelocityAt(0) - velocities[i]), 0, 40);
            window.Hide();
            Assert.Null(Get<EventHandler?>(window, "_frame"));
        }
        finally { window.Close(); }
    });

    internal static GreetingAcceptanceTests.MainWindowFixture Create() => new("en", false,
        settings => { settings.EnableLocalOnlyMode = true; settings.DisableMouseLeaveAutoClose = true; },
        services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));

    internal static void Invoke(object instance, string name, params object?[] arguments) =>
        instance.GetType().GetMethod(name, Private)!.Invoke(instance, arguments);
    internal static void Set(object instance, string name, object value) =>
        instance.GetType().GetField(name, Private)!.SetValue(instance, value);
    internal static T Get<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
}
