namespace VNotch.WinUI.Motion;

/// <summary>Where the notch takes the mouse. Everywhere else in its window, clicks go to the windows below.</summary>
public static class HitTest
{
    /// <param name="x">Cursor in DIP from the window's left edge.</param>
    /// <param name="y">Cursor in DIP from the window's top edge (the screen's top).</param>
    /// <param name="windowWidth">Window width in DIP; the notch hangs centred in it.</param>
    /// <param name="scaleX">The hover stretch, scaled about the notch's top centre.</param>
    public static bool InsideNotch(double x, double y, double windowWidth, double width, double height, double scaleX = 1)
    {
        if (y < 0 || y > height) return false;
        return Math.Abs(x - windowWidth / 2) <= width * scaleX / 2;
    }
}
