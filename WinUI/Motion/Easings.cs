namespace VNotch.WinUI.Motion;

/// <summary>
/// The easing curves of the WPF notch, as plain functions of normalized time. They have no WinUI types so
/// the test project can check them against the WPF easings they copy.
/// </summary>
public static class Easings
{
    // AnimationPrimitives._easeAppleOut: CSS cubic-bezier(0.32, 0.72, 0, 1).
    public static double AppleOut(double t) => CubicBezier(t, 0.32, 0.72, 0.0, 1.0);

    // WPF ExponentialEase { Exponent = 6, EaseOut }.
    public static double ExpOut6(double t) => 1 - ExponentialIn(1 - t, 6);

    // WPF PowerEase { Power = 3, EaseOut }.
    public static double PowerOut3(double t) => 1 - Math.Pow(1 - Clamp(t), 3);

    // WPF QuadraticEase { EaseOut }.
    public static double QuadOut(double t) => 1 - (1 - Clamp(t)) * (1 - Clamp(t));

    // WPF PowerEase { Power = 2, EaseIn }.
    public static double PowerIn2(double t) => Clamp(t) * Clamp(t);

    // DampedSpringEase() as EaseOut: the native step response with zeta 0.82, normalized to land on 1.
    public static double SoftSpring(double t) => DampedSpring(t, 0.82);

    public static double Linear(double t) => Clamp(t);

    private const double SpringNaturalFrequency = 12.0;

    public static double DampedSpring(double t, double dampingRatio)
    {
        double time = Clamp(t);
        if (time == 0 || time == 1) return time;
        double dampedFrequency = SpringNaturalFrequency * Math.Sqrt(1 - dampingRatio * dampingRatio);
        return StepResponse(dampingRatio, dampedFrequency, time) / StepResponse(dampingRatio, dampedFrequency, 1);
    }

    private static double StepResponse(double zeta, double dampedFrequency, double t)
    {
        if (zeta == 1.0)
            return 1 - Math.Exp(-SpringNaturalFrequency * t) * (1 + SpringNaturalFrequency * t);
        double decay = Math.Exp(-zeta * SpringNaturalFrequency * t);
        double phase = dampedFrequency * t;
        return 1 - decay * (Math.Cos(phase) + zeta * SpringNaturalFrequency / dampedFrequency * Math.Sin(phase));
    }

    private static double ExponentialIn(double t, double exponent) =>
        (Math.Exp(exponent * Clamp(t)) - 1) / (Math.Exp(exponent) - 1);

    private static double CubicBezier(double x, double x1, double y1, double x2, double y2)
    {
        x = Clamp(x);
        if (x == 0 || x == 1) return x;
        double lower = 0, upper = 1, t = x;
        for (int i = 0; i < 40; i++)
        {
            double error = Sample(t, x1, x2) - x;
            if (Math.Abs(error) < 1e-10) break;
            if (error < 0) lower = t; else upper = t;
            double slope = Derivative(t, x1, x2);
            double next = Math.Abs(slope) > 1e-8 ? t - error / slope : double.NaN;
            t = next > lower && next < upper ? next : (lower + upper) / 2;
        }
        return Sample(t, y1, y2);
    }

    private static double Sample(double t, double p1, double p2)
    {
        double mt = 1 - t;
        return 3 * mt * mt * t * p1 + 3 * mt * t * t * p2 + t * t * t;
    }

    private static double Derivative(double t, double p1, double p2)
    {
        double mt = 1 - t;
        return 3 * mt * mt * p1 + 6 * mt * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }

    private static double Clamp(double t) => Math.Clamp(t, 0, 1);
}
