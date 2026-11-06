using System.Globalization;
using System.Numerics;
using Microsoft.UI.Composition;
using VNotch.WinUI.Motion;

namespace VNotch.WinUI.Composition;

/// <summary>
/// Composition animations with the WPF notch's curves. Composition has no custom easing, so a curve is sampled
/// into evenly spaced linear key frames; 48 of them keep the error well under a pixel for these short moves.
/// </summary>
internal static class Anim
{
    private const int Samples = 48;

    /// <summary>A key-frame animation from <paramref name="from"/> (or the current value when null) to <paramref name="to"/>.</summary>
    public static ScalarKeyFrameAnimation Curve(Compositor c, float? from, float to, double ms, Func<double, double> ease, double delayMs = 0)
    {
        var a = c.CreateScalarKeyFrameAnimation();
        var linear = c.CreateLinearEasingFunction();
        if (from is float start)
        {
            a.InsertKeyFrame(0, start, linear);
            for (int i = 1; i <= Samples; i++)
            {
                double t = (double)i / Samples;
                a.InsertKeyFrame((float)t, (float)(start + (to - start) * ease(t)), linear);
            }
        }
        else
        {
            a.InsertExpressionKeyFrame(0, "this.StartingValue", linear);
            string target = F(to);
            for (int i = 1; i <= Samples; i++)
            {
                double t = (double)i / Samples;
                a.InsertExpressionKeyFrame((float)t, $"this.StartingValue + ({target} - this.StartingValue) * {F(ease(t))}", linear);
            }
        }
        a.Duration = TimeSpan.FromMilliseconds(Math.Max(1, ms));
        if (delayMs > 0)
        {
            a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
            a.DelayBehavior = from.HasValue ? AnimationDelayBehavior.SetInitialValueBeforeDelay : AnimationDelayBehavior.SetInitialValueAfterDelay;
        }
        return a;
    }

    public static void Run(CompositionObject target, string property, float? from, float to, double ms, Func<double, double> ease, double delayMs = 0) =>
        target.StartAnimation(property, Curve(target.Compositor, from, to, ms, ease, delayMs));

    public static SpringVector2NaturalMotionAnimation Spring(Compositor c, Vector2 to, TimeSpan response, double bounce)
    {
        var (period, damping) = Springs.For(response, bounce);
        var s = c.CreateSpringVector2Animation();
        s.FinalValue = to;
        s.Period = period;
        s.DampingRatio = damping;
        return s;
    }

    public static SpringScalarNaturalMotionAnimation Spring(Compositor c, float to, TimeSpan response, double bounce)
    {
        var (period, damping) = Springs.For(response, bounce);
        var s = c.CreateSpringScalarAnimation();
        s.FinalValue = to;
        s.Period = period;
        s.DampingRatio = damping;
        return s;
    }

    /// <summary>Keeps a property bound to an expression over the parameters, for as long as the target lives.</summary>
    public static void Bind(CompositionObject target, string property, string expression, params (string Name, CompositionObject Value)[] references)
    {
        var e = target.Compositor.CreateExpressionAnimation(expression);
        foreach (var (name, value) in references) e.SetReferenceParameter(name, value);
        target.StartAnimation(property, e);
    }

    public static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
