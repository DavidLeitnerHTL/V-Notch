using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VNotch.Controls;

/// <summary>A sun peeking out behind a cloud, for the weather tab in the nav bar and the tab settings.</summary>
public static class WeatherGlyph
{
    // On a 20×20 grid like the other tab icons. Three arcs make the cloud; the sun sits up left behind it.
    private const string CloudData = "M6.5,17 L15.5,17 A3.5,3.5 0 1 0 15.25,10.01 A4.5,4.5 0 0 0 6.53,11 A3,3 0 0 0 6.5,17 Z";

    public static Geometry Data { get; } = CreateData();

    /// <summary>The same shape as path markup, for places that take a string.</summary>
    public static string PathData { get; } = ((IFormattable)Data.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute))
        .ToString(null, CultureInfo.InvariantCulture);

    private static Geometry CreateData()
    {
        var cloud = Geometry.Parse(CloudData);
        var sun = new GeometryGroup { FillRule = FillRule.Nonzero };
        var center = new Point(7, 7.4);
        sun.Children.Add(new EllipseGeometry(center, 3, 3));
        var rayPen = new Pen(Brushes.Black, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4;
            var from = new Point(center.X + Math.Cos(angle) * 4.6, center.Y + Math.Sin(angle) * 4.6);
            var to = new Point(center.X + Math.Cos(angle) * 5.9, center.Y + Math.Sin(angle) * 5.9);
            sun.Children.Add(new LineGeometry(from, to).GetWidenedPathGeometry(rayPen));
        }
        // A thin gap keeps the cloud's outline readable where it covers the sun.
        var gap = Geometry.Combine(cloud, cloud.GetWidenedPathGeometry(new Pen(Brushes.Black, 2.4)), GeometryCombineMode.Union, null);
        var visibleSun = Geometry.Combine(sun, gap, GeometryCombineMode.Exclude, null);
        var result = Geometry.Combine(visibleSun, cloud, GeometryCombineMode.Union, null);
        result.Freeze();
        return result;
    }
}
