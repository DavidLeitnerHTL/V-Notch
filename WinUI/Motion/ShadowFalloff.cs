namespace VNotch.WinUI.Motion;

/// <summary>
/// The notch shadow of the WPF notch (Controls/NotchSoftShadow): a Gaussian falloff with sigma = 20 / 3, shifted
/// down by 4 DIP, at most 60 % black.
/// </summary>
public static class ShadowFalloff
{
    public const double BlurRadius = 20;
    public const double Depth = 4;
    public const double Strength = 0.6;
    public const double Sigma = BlurRadius / 3;
    public const double Reach = 3 * Sigma;
    // Bands reach this far under the silhouette, past the part the notch itself hides.
    public const double Inset = Depth + 1;
    public const double Band = Reach + Inset;
    public const int Stops = 16;

    /// <summary>Shadow opacity at a signed distance from the shifted silhouette's edge (negative = inside).</summary>
    public static double AlphaAt(double distance) => Strength * (1 - NormalCdf(distance / Sigma));

    /// <summary>Distance from the silhouette edge for band stop i, from the outer edge (Reach) to Inset inside.</summary>
    public static double BandDistance(int i) => Reach - (double)i / Stops * Band;

    // Abramowitz-Stegun 7.1.26, as in NotchSoftShadow.
    private static double NormalCdf(double x)
    {
        double z = Math.Abs(x) / Math.Sqrt(2);
        double t = 1 / (1 + 0.3275911 * z);
        double erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-z * z);
        return 0.5 * (1 + Math.Sign(x) * erf);
    }
}
