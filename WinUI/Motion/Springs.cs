namespace VNotch.WinUI.Motion;

/// <summary>
/// The WPF notch describes its springs SwiftUI-style (response, bounce). Composition's natural-motion springs take
/// a Period, which is the time constant 1 / omega, and a damping ratio.
/// </summary>
public static class Springs
{
    // AnimationPrimitives.ShellOpenSpring and ShellCloseSpring.
    public static readonly TimeSpan OpenResponse = TimeSpan.FromMilliseconds(400);
    public const double OpenBounce = 0.2;
    public static readonly TimeSpan CloseResponse = TimeSpan.FromMilliseconds(262);
    public const double CloseBounce = 0;

    public static (TimeSpan Period, float DampingRatio) For(TimeSpan response, double bounce)
    {
        double omega = 2 * Math.PI / response.TotalSeconds;
        return (TimeSpan.FromSeconds(1 / omega), (float)Math.Clamp(1 - bounce, 0.05, 1));
    }
}
