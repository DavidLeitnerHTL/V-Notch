using System.Windows.Media;

namespace VNotch.Controls;

/// <summary>A π, for the tab that shows the user's Raspberry Pi dashboard, in the nav bar and the tab settings.</summary>
public static class PiGlyph
{
    // On a 20×20 grid like the other tab icons: the bar, a straight left leg, a right leg with a foot.
    public const string PathData =
        "M2,4.2 C2,3.4 2.6,2.8 3.4,2.8 L17.6,2.8 C18.4,2.8 19,3.4 19,4.2 C19,5 18.4,5.6 17.6,5.6 L3.4,5.6 C2.6,5.6 2,5 2,4.2 Z " +
        "M6,5.6 L8.6,5.6 L8.6,16.4 C8.6,17.2 8,17.8 7.3,17.8 C6.6,17.8 6,17.2 6,16.4 Z " +
        "M12.4,5.6 L15,5.6 L15,14.2 C15,14.9 15.4,15.2 16,15.2 L17.2,15.2 L17.2,17.8 L15.8,17.8 C13.6,17.8 12.4,16.6 12.4,14.4 Z";

    public static Geometry Data { get; } = CreateData();

    private static Geometry CreateData()
    {
        var geometry = Geometry.Parse(PathData);
        geometry.Freeze();
        return geometry;
    }
}
