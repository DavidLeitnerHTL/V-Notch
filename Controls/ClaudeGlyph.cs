using System.Windows.Media;

namespace VNotch.Controls;

/// <summary>The starburst that stands for Claude in the nav bar, the tab settings and the notch's alerts.</summary>
public static class ClaudeGlyph
{
    public const string PathData =
        "M10,0.4 L10.54,7.97 L14.8,1.69 L11.48,8.52 L18.31,5.2 L12.03,9.46 L19.6,10 L12.03,10.54 L18.31,14.8 " +
        "L11.48,11.48 L14.8,18.31 L10.54,12.03 L10,19.6 L9.46,12.03 L5.2,18.31 L8.52,11.48 L1.69,14.8 " +
        "L7.97,10.54 L0.4,10 L7.97,9.46 L1.69,5.2 L8.52,8.52 L5.2,1.69 L9.46,7.97 Z";

    public static Geometry Data { get; } = CreateData();

    private static Geometry CreateData()
    {
        var geometry = Geometry.Parse(PathData);
        geometry.Freeze();
        return geometry;
    }
}
