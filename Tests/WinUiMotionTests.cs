using System.Windows.Media.Animation;
using VNotch.Controls;
using VNotch.Services;
using VNotch.WinUI.Motion;
using Xunit;

namespace VNotch.Tests;

/// <summary>The WinUI notch must move exactly like the WPF one: same curves, springs, shadow and hit area.</summary>
public sealed class WinUiMotionTests
{
    public static TheoryData<string> Curves => new() { "AppleOut", "ExpOut6", "PowerOut3", "PowerIn2", "QuadOut", "SoftSpring" };

    [Theory]
    [MemberData(nameof(Curves))]
    public void EasingsMatchTheWpfCurves(string name)
    {
        var (port, wpf) = Curve(name);
        for (int i = 0; i <= 100; i++)
        {
            double t = i / 100.0;
            Assert.Equal(wpf.Ease(t), port(t), tolerance: 1e-6);
        }
    }

    private static (Func<double, double> Port, IEasingFunction Wpf) Curve(string name) => name switch
    {
        "AppleOut" => (Easings.AppleOut, AnimationPrimitives._easeAppleOut),
        "ExpOut6" => (Easings.ExpOut6, AnimationPrimitives._easeExpOut6),
        "PowerOut3" => (Easings.PowerOut3, AnimationPrimitives._easePowerOut3),
        "PowerIn2" => (Easings.PowerIn2, AnimationPrimitives._easePowerIn2),
        "QuadOut" => (Easings.QuadOut, AnimationPrimitives._easeQuadOut),
        _ => (Easings.SoftSpring, AnimationPrimitives._easeSoftSpring),
    };

    [Fact]
    public void ShellSpringsBecomeCompositionPeriods()
    {
        var (openPeriod, openDamping) = Springs.For(Springs.OpenResponse, Springs.OpenBounce);
        var (closePeriod, closeDamping) = Springs.For(Springs.CloseResponse, Springs.CloseBounce);

        Assert.Equal(400 / (2 * Math.PI), openPeriod.TotalMilliseconds, 2);
        Assert.Equal(0.8f, openDamping, 4);
        Assert.Equal(262 / (2 * Math.PI), closePeriod.TotalMilliseconds, 2);
        Assert.Equal(1f, closeDamping, 4);
        // The WinUI constants are the WPF shell springs.
        Assert.Equal(Springs.OpenResponse, AnimationPrimitives.ShellOpenSpring.Response);
        Assert.Equal(Springs.OpenBounce, AnimationPrimitives.ShellOpenSpring.Bounce);
        Assert.Equal(Springs.CloseResponse, AnimationPrimitives.ShellCloseSpring.Response);
        Assert.Equal(Springs.CloseBounce, AnimationPrimitives.ShellCloseSpring.Bounce);
    }

    [Fact]
    public void ShadowFalloffIsThePaintedWpfShadow()
    {
        for (double d = -8; d <= 25; d += 0.5)
            Assert.Equal(NotchSoftShadow.AlphaAt(d), ShadowFalloff.AlphaAt(d), 9);
        Assert.Equal(NotchSoftShadow.BlurRadius, ShadowFalloff.BlurRadius);
        Assert.Equal(NotchSoftShadow.Depth, ShadowFalloff.Depth);
        Assert.Equal(NotchSoftShadow.Strength, ShadowFalloff.Strength);
    }

    [Fact]
    public void OnlyTheNotchTakesTheMouse()
    {
        // Closed notch 230 × 34 in a 640 DIP window: centre in, just beside or below it out.
        Assert.True(HitTest.InsideNotch(320, 10, 640, 230, 34));
        Assert.True(HitTest.InsideNotch(320 - 115, 34, 640, 230, 34));
        Assert.False(HitTest.InsideNotch(320 - 116, 10, 640, 230, 34));
        Assert.False(HitTest.InsideNotch(320, 35, 640, 230, 34));
        Assert.False(HitTest.InsideNotch(320, -1, 640, 230, 34));
        // The hover stretch (108 %) widens the area the notch takes.
        Assert.True(HitTest.InsideNotch(320 - 120, 10, 640, 230, 34, 1.08));
    }
}
