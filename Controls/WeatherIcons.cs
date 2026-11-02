using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VNotch.Controls;

/// <summary>What a WMO weather code looks like, coarsely, for the scene and the icons.</summary>
internal enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, HeavyRain, Sleet, Snow, Thunder }

/// <summary>Small coloured weather icons on a 20×20 grid, plus the shared shapes and colours of the weather tab.</summary>
internal static class WeatherIcons
{
    public static readonly Color SunColor = Color.FromRgb(0xFF, 0xC5, 0x31);
    public static readonly Color MoonColor = Color.FromRgb(0xEE, 0xEA, 0xD8);
    public static readonly Color CloudColor = Color.FromRgb(0xE9, 0xEE, 0xF5);
    public static readonly Color DarkCloudColor = Color.FromRgb(0x9A, 0xA5, 0xB4);
    public static readonly Color RainColor = Color.FromRgb(0x5A, 0xB0, 0xFF);
    public static readonly Color BoltColor = Color.FromRgb(0xFF, 0xD6, 0x0A);

    /// <summary>The cloud on the 20×20 grid: flat base from x 5.5 to 14.5 at y 16, three lobes.</summary>
    public static Geometry Cloud { get; } = Frozen(Geometry.Parse(
        "M5.5,16 L14.5,16 A3.5,3.5 0 1 0 14.25,9.01 A4.5,4.5 0 0 0 5.53,10 A3,3 0 0 0 5.5,16 Z"));

    public static Geometry Bolt { get; } = Frozen(Geometry.Parse("M10.8,11.5 L7.6,16.4 L10,16.4 L8.9,19.6 L13.1,14.2 L10.6,14.2 L12,11.5 Z"));

    public static WeatherKind KindOf(int code) => code switch
    {
        0 or 1 => WeatherKind.Clear,
        2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 55 => WeatherKind.Drizzle,
        56 or 57 or 66 or 67 => WeatherKind.Sleet,
        61 or 63 or 80 or 81 => WeatherKind.Rain,
        65 or 82 => WeatherKind.HeavyRain,
        >= 71 and <= 77 or 85 or 86 => WeatherKind.Snow,
        >= 95 => WeatherKind.Thunder,
        _ => WeatherKind.Cloudy
    };

    public static bool IsWet(WeatherKind kind) =>
        kind is WeatherKind.Drizzle or WeatherKind.Rain or WeatherKind.HeavyRain or WeatherKind.Sleet or WeatherKind.Snow or WeatherKind.Thunder;

    /// <summary>A static icon for one forecast step or day.</summary>
    public static FrameworkElement Create(int code, bool isDay, double size)
    {
        var canvas = new Canvas { Width = 20, Height = 20 };
        var kind = KindOf(code);
        switch (kind)
        {
            case WeatherKind.Clear:
                if (isDay) AddSun(canvas, 10, 10, 1);
                else AddMoon(canvas, 10, 10, 1);
                break;
            case WeatherKind.PartlyCloudy:
                if (isDay) AddSun(canvas, 7, 7, 0.72);
                else AddMoon(canvas, 7.5, 7, 0.7);
                AddCloud(canvas, 1.2, 2, CloudColor, outline: true);
                break;
            case WeatherKind.Cloudy:
                AddCloud(canvas, -3.4, -2.6, DarkCloudColor, outline: false, scale: 0.8);
                AddCloud(canvas, 0.6, 0.6, CloudColor, outline: true);
                break;
            case WeatherKind.Fog:
                AddCloud(canvas, 0, -3.5, DarkCloudColor, outline: false);
                AddBar(canvas, 3.5, 15, 13);
                AddBar(canvas, 6, 18.2, 10);
                break;
            case WeatherKind.Thunder:
                AddCloud(canvas, 0, -3.5, DarkCloudColor, outline: false);
                canvas.Children.Add(new Path { Data = Bolt, Fill = Brush(BoltColor) });
                break;
            default:
                AddCloud(canvas, 0, -3.5, kind == WeatherKind.HeavyRain ? DarkCloudColor : CloudColor, outline: false);
                AddPrecipitation(canvas, kind);
                break;
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
    }

    private static void AddSun(Canvas canvas, double cx, double cy, double scale)
    {
        var rays = new GeometryGroup();
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            rays.Children.Add(new LineGeometry(
                new Point(cx + Math.Cos(a) * 6.2 * scale, cy + Math.Sin(a) * 6.2 * scale),
                new Point(cx + Math.Cos(a) * 8.3 * scale, cy + Math.Sin(a) * 8.3 * scale)));
        }
        canvas.Children.Add(new Path
        {
            Data = rays,
            Stroke = Brush(SunColor),
            StrokeThickness = 1.6 * scale,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        canvas.Children.Add(new Path { Data = new EllipseGeometry(new Point(cx, cy), 4.1 * scale, 4.1 * scale), Fill = Brush(SunColor) });
    }

    private static void AddMoon(Canvas canvas, double cx, double cy, double scale)
    {
        var moon = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(cx, cy), 6.4 * scale, 6.4 * scale),
            new EllipseGeometry(new Point(cx + 3.6 * scale, cy - 2.6 * scale), 5.4 * scale, 5.4 * scale));
        canvas.Children.Add(new Path { Data = moon, Fill = Brush(MoonColor) });
    }

    private static void AddCloud(Canvas canvas, double dx, double dy, Color color, bool outline, double scale = 1)
    {
        var path = new Path
        {
            Data = Cloud,
            Fill = Brush(color),
            RenderTransform = new MatrixTransform(scale, 0, 0, scale, dx, dy)
        };
        if (outline)
        {
            // Black like the notch: separates the cloud from what is behind it.
            path.Stroke = Brushes.Black;
            path.StrokeThickness = 1.3;
        }
        canvas.Children.Add(path);
    }

    private static void AddBar(Canvas canvas, double x, double y, double width)
    {
        canvas.Children.Add(new Line
        {
            X1 = x, Y1 = y, X2 = x + width, Y2 = y,
            Stroke = Brush(DarkCloudColor),
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
    }

    private static void AddPrecipitation(Canvas canvas, WeatherKind kind)
    {
        double[] xs = { 7, 10.5, 14 };
        for (int i = 0; i < xs.Length; i++)
        {
            double x = xs[i];
            bool flake = kind == WeatherKind.Snow || (kind == WeatherKind.Sleet && i == 1);
            if (flake)
            {
                canvas.Children.Add(new Path { Data = new EllipseGeometry(new Point(x - 0.5, 16 + (i % 2) * 1.6), 1.25, 1.25), Fill = Brushes.White });
            }
            else if (kind == WeatherKind.Drizzle)
            {
                canvas.Children.Add(new Path { Data = new EllipseGeometry(new Point(x - 0.4, 16 + (i % 2) * 1.6), 0.9, 0.9), Fill = Brush(RainColor) });
            }
            else
            {
                canvas.Children.Add(new Line
                {
                    X1 = x, Y1 = 14.6, X2 = x - 1.1, Y2 = 17.8 + (kind == WeatherKind.HeavyRain ? 0.8 : 0),
                    Stroke = Brush(RainColor),
                    StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                });
            }
        }
    }

    public static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Where a temperature sits on the cold-to-hot scale the curve and the day bars share.</summary>
    private static readonly (double T, uint Rgb)[] TemperatureStops =
    {
        (-12, 0xA5B4FF), (-2, 0x6BA8FF), (5, 0x4FC3F7), (11, 0x4FD1B5), (16, 0x9BE15D), (21, 0xFFD54A), (27, 0xFF9F43), (34, 0xFF5A5A)
    };

    public static Color TemperatureColor(double celsius)
    {
        var stops = TemperatureStops;
        if (celsius <= stops[0].T) return FromRgb(stops[0].Rgb);
        for (int i = 1; i < stops.Length; i++)
        {
            if (celsius > stops[i].T) continue;
            double f = (celsius - stops[i - 1].T) / (stops[i].T - stops[i - 1].T);
            return Lerp(FromRgb(stops[i - 1].Rgb), FromRgb(stops[i].Rgb), f);
        }
        return FromRgb(stops[^1].Rgb);
    }

    public static Color Lerp(Color a, Color b, double f) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * f),
        (byte)Math.Round(a.R + (b.R - a.R) * f),
        (byte)Math.Round(a.G + (b.G - a.G) * f),
        (byte)Math.Round(a.B + (b.B - a.B) * f));

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color FromRgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static Geometry Frozen(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}
