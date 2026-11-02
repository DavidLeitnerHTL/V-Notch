using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VNotch.Controls;

/// <summary>
/// The animated sky next to the temperature, drawn on a 100×100 canvas: a turning, breathing sun; a rocking moon
/// under twinkling stars; clouds drifting at two depths; rain with splashes, swaying snow, lightning, fog banks
/// and gusts of wind. Every endless motion goes into the given <see cref="LoopSet"/>.
/// </summary>
public sealed partial class WeatherView
{
    private const double WindyKmh = 25;

    private enum CloudTone { Light, Grey, Dark }

    private static FrameworkElement BuildSky(WeatherKind kind, bool isDay, double windKmh, LoopSet loops)
    {
        var c = new Canvas { Width = 100, Height = 100, ClipToBounds = false };
        var random = new Random(17);

        AddGlow(c, GlowColor(kind, isDay), loops);
        switch (kind)
        {
            case WeatherKind.Clear:
                if (isDay) AddSun(c, 50, 48, 1, loops);
                else
                {
                    AddStars(c, random, loops);
                    AddMoon(c, 50, 48, 1, loops);
                }
                break;
            case WeatherKind.PartlyCloudy:
                if (isDay) AddSun(c, 37, 37, 0.78, loops);
                else
                {
                    AddStars(c, random, loops);
                    AddMoon(c, 39, 37, 0.74, loops);
                }
                AddCloud(c, 58, 82, 3.6, CloudTone.Light, loops, drift: 4.5, seconds: 5.5);
                break;
            case WeatherKind.Cloudy:
                AddCloud(c, 38, 60, 3.0, CloudTone.Grey, loops, drift: -5, seconds: 7.5, opacity: 0.8);
                AddCloud(c, 56, 82, 4.0, CloudTone.Light, loops, drift: 4, seconds: 5.8);
                break;
            case WeatherKind.Fog:
                AddCloud(c, 50, 56, 3.6, CloudTone.Grey, loops, drift: 3, seconds: 7, opacity: 0.55);
                AddFog(c, loops);
                break;
            case WeatherKind.Drizzle:
                AddCloud(c, 48, 66, 4.2, CloudTone.Light, loops, drift: 3.5, seconds: 6);
                AddDrops(c, random, 9, 1.15, 1.45, 1.6, 5, splash: false, loops);
                break;
            case WeatherKind.Rain:
                AddCloud(c, 66, 56, 3.0, CloudTone.Dark, loops, drift: -4, seconds: 7, opacity: 0.85);
                AddCloud(c, 46, 66, 4.2, CloudTone.Grey, loops, drift: 3.5, seconds: 5.5);
                AddDrops(c, random, 15, 0.62, 0.82, 2.2, 10, splash: true, loops);
                break;
            case WeatherKind.HeavyRain:
                AddCloud(c, 66, 56, 3.0, CloudTone.Dark, loops, drift: -4, seconds: 6.5, opacity: 0.9);
                AddCloud(c, 46, 66, 4.3, CloudTone.Dark, loops, drift: 3.5, seconds: 5);
                AddDrops(c, random, 24, 0.44, 0.6, 2.6, 13, splash: true, loops);
                break;
            case WeatherKind.Sleet:
                AddCloud(c, 46, 66, 4.2, CloudTone.Grey, loops, drift: 3.5, seconds: 6);
                AddDrops(c, random, 8, 0.7, 0.9, 2, 8, splash: true, loops);
                AddFlakes(c, random, 7, loops);
                break;
            case WeatherKind.Snow:
                AddCloud(c, 48, 64, 4.2, CloudTone.Light, loops, drift: 3, seconds: 6.5);
                AddFlakes(c, random, 15, loops);
                break;
            case WeatherKind.Thunder:
                AddFlash(c, loops);
                AddCloud(c, 66, 56, 3.0, CloudTone.Dark, loops, drift: -4, seconds: 6.5, opacity: 0.9);
                AddBolt(c, loops);
                AddCloud(c, 46, 66, 4.3, CloudTone.Dark, loops, drift: 3, seconds: 5.2);
                AddDrops(c, random, 12, 0.5, 0.68, 2.3, 11, splash: true, loops);
                break;
        }
        if (windKmh >= WindyKmh) AddWind(c, loops);
        return new Viewbox { Width = 108, Height = 108, Child = c, Stretch = Stretch.Uniform };
    }

    private static Color GlowColor(WeatherKind kind, bool isDay) => kind switch
    {
        WeatherKind.Clear => isDay ? Color.FromRgb(0xFF, 0xA8, 0x1F) : Color.FromRgb(0x8C, 0x7C, 0xFF),
        WeatherKind.PartlyCloudy => isDay ? Color.FromRgb(0xFF, 0xBE, 0x4D) : Color.FromRgb(0x7C, 0x8C, 0xFF),
        WeatherKind.Thunder => Color.FromRgb(0x9D, 0x7C, 0xFF),
        WeatherKind.Rain or WeatherKind.HeavyRain or WeatherKind.Drizzle or WeatherKind.Sleet => Color.FromRgb(0x3D, 0x8B, 0xFF),
        WeatherKind.Snow => Color.FromRgb(0xA8, 0xD8, 0xFF),
        WeatherKind.Fog => Color.FromRgb(0xB8, 0xC0, 0xCC),
        _ => Color.FromRgb(0x8F, 0xA3, 0xBF)
    };

    private static void AddGlow(Canvas c, Color color, LoopSet loops)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(WeatherIcons.WithAlpha(color, 0x58), 0));
        brush.GradientStops.Add(new GradientStop(WeatherIcons.WithAlpha(color, 0x1C), 0.55));
        brush.GradientStops.Add(new GradientStop(WeatherIcons.WithAlpha(color, 0x00), 1));
        brush.Freeze();
        var glow = new Ellipse { Width = 150, Height = 150, Fill = brush, IsHitTestVisible = false };
        Canvas.SetLeft(glow, -25);
        Canvas.SetTop(glow, -27);
        var scale = new ScaleTransform(1, 1, 75, 75);
        glow.RenderTransform = scale;
        c.Children.Add(glow);
        loops.Swing(scale, ScaleTransform.ScaleXProperty, 0.9, 1.07, 4.2);
        loops.Swing(scale, ScaleTransform.ScaleYProperty, 0.9, 1.07, 4.2);
        loops.Swing(glow, UIElement.OpacityProperty, 0.7, 1, 3.1);
    }

    private static void AddSun(Canvas c, double cx, double cy, double k, LoopSet loops)
    {
        // A soft halo that breathes against the core.
        var haloBrush = new RadialGradientBrush();
        haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0xFF, 0xC5, 0x31), 0.35));
        haloBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xC5, 0x31), 1));
        haloBrush.Freeze();
        double haloR = 34 * k;
        var halo = new Ellipse { Width = haloR * 2, Height = haloR * 2, Fill = haloBrush };
        Canvas.SetLeft(halo, cx - haloR);
        Canvas.SetTop(halo, cy - haloR);
        var haloScale = new ScaleTransform(1, 1, haloR, haloR);
        halo.RenderTransform = haloScale;
        c.Children.Add(halo);
        loops.Swing(haloScale, ScaleTransform.ScaleXProperty, 1.08, 0.9, 2.6);
        loops.Swing(haloScale, ScaleTransform.ScaleYProperty, 1.08, 0.9, 2.6);

        // Twelve rays turning slowly round the core, their ring pulsing.
        var rays = new Canvas { IsHitTestVisible = false };
        Canvas.SetLeft(rays, cx);
        Canvas.SetTop(rays, cy);
        double inner = 23 * k, length = 9 * k, width = 4 * k;
        var rayBrush = WeatherIcons.Brush(Color.FromArgb(0xEE, 0xFF, 0xC5, 0x31));
        for (int i = 0; i < 12; i++)
        {
            bool longRay = i % 2 == 0;
            double len = longRay ? length : length * 0.62;
            var ray = new Rectangle { Width = width, Height = len, RadiusX = width / 2, RadiusY = width / 2, Fill = rayBrush };
            Canvas.SetLeft(ray, -width / 2);
            Canvas.SetTop(ray, -(inner + len));
            ray.RenderTransform = new RotateTransform(i * 30, width / 2, inner + len);
            rays.Children.Add(ray);
        }
        var raySpin = new RotateTransform(0);
        var rayPulse = new ScaleTransform(1, 1);
        var rayGroup = new TransformGroup();
        rayGroup.Children.Add(rayPulse);
        rayGroup.Children.Add(raySpin);
        rays.RenderTransform = rayGroup;
        c.Children.Add(rays);
        loops.Run(raySpin, RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(36)) { RepeatBehavior = RepeatBehavior.Forever });
        loops.Swing(rayPulse, ScaleTransform.ScaleXProperty, 0.92, 1.06, 2.2);
        loops.Swing(rayPulse, ScaleTransform.ScaleYProperty, 0.92, 1.06, 2.2);

        var core = new RadialGradientBrush { GradientOrigin = new Point(0.36, 0.3), Center = new Point(0.42, 0.38), RadiusX = 0.7, RadiusY = 0.7 };
        core.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xF6, 0xC4), 0));
        core.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xCB, 0x3D), 0.55));
        core.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xA2, 0x14), 1));
        core.Freeze();
        double r = 17 * k;
        var disc = new Ellipse { Width = r * 2, Height = r * 2, Fill = core };
        Canvas.SetLeft(disc, cx - r);
        Canvas.SetTop(disc, cy - r);
        var discScale = new ScaleTransform(1, 1, r, r);
        disc.RenderTransform = discScale;
        c.Children.Add(disc);
        loops.Swing(discScale, ScaleTransform.ScaleXProperty, 0.97, 1.04, 3);
        loops.Swing(discScale, ScaleTransform.ScaleYProperty, 0.97, 1.04, 3);
    }

    private static void AddMoon(Canvas c, double cx, double cy, double k, LoopSet loops)
    {
        var halo = new RadialGradientBrush();
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 0xD8, 0xD4, 0xFF), 0.3));
        halo.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xD8, 0xD4, 0xFF), 1));
        halo.Freeze();
        double haloR = 32 * k;
        var haloDisc = new Ellipse { Width = haloR * 2, Height = haloR * 2, Fill = halo };
        Canvas.SetLeft(haloDisc, cx - haloR);
        Canvas.SetTop(haloDisc, cy - haloR);
        c.Children.Add(haloDisc);
        loops.Swing(haloDisc, UIElement.OpacityProperty, 0.55, 1, 3.4);

        double r = 18 * k;
        var shape = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(r, r), r, r),
            new EllipseGeometry(new Point(r + 9.5 * k, r - 7 * k), 15 * k, 15 * k));
        shape.Freeze();
        var fill = new LinearGradientBrush(Color.FromRgb(0xFF, 0xFD, 0xF2), Color.FromRgb(0xDD, 0xD5, 0xB8), new Point(0, 0), new Point(1, 1));
        fill.Freeze();
        var moon = new Path { Data = shape, Fill = fill };
        var holder = new Canvas { Width = r * 2, Height = r * 2 };
        holder.Children.Add(moon);
        // Two faint craters inside the crescent.
        foreach (var (x, y, d) in new[] { (r * 0.55, r * 1.25, 4.2 * k), (r * 0.8, r * 1.62, 2.6 * k) })
        {
            var crater = new Ellipse { Width = d, Height = d, Fill = WeatherIcons.Brush(Color.FromArgb(0x26, 0x8A, 0x80, 0x60)) };
            Canvas.SetLeft(crater, x - d / 2);
            Canvas.SetTop(crater, y - d / 2);
            holder.Children.Add(crater);
        }
        Canvas.SetLeft(holder, cx - r);
        Canvas.SetTop(holder, cy - r);
        var rock = new RotateTransform(0, r, r);
        var bob = new TranslateTransform();
        var group = new TransformGroup();
        group.Children.Add(rock);
        group.Children.Add(bob);
        holder.RenderTransform = group;
        c.Children.Add(holder);
        loops.Swing(rock, RotateTransform.AngleProperty, -7, 5, 6);
        loops.Swing(bob, TranslateTransform.YProperty, -1.6, 1.6, 4.4);
    }

    private static readonly Geometry StarShape = CreateStarShape();

    private static Geometry CreateStarShape()
    {
        var geometry = Geometry.Parse("M0,-1 C0.12,-0.12 0.12,-0.12 1,0 C0.12,0.12 0.12,0.12 0,1 C-0.12,0.12 -0.12,0.12 -1,0 C-0.12,-0.12 -0.12,-0.12 0,-1 Z");
        geometry.Freeze();
        return geometry;
    }

    private static void AddStars(Canvas c, Random random, LoopSet loops)
    {
        (double X, double Y, double R)[] stars =
        {
            (12, 16, 3.4), (86, 12, 2.8), (80, 40, 2.2), (16, 64, 2.4), (92, 74, 3), (30, 6, 2), (64, 8, 2.4), (6, 40, 1.8), (70, 88, 1.8)
        };
        foreach (var (x, y, r) in stars)
        {
            var star = new Path { Data = StarShape, Fill = Brushes.White, Opacity = 0.2 };
            var scale = new ScaleTransform(r, r);
            var pulse = new ScaleTransform(1, 1);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(pulse);
            group.Children.Add(new TranslateTransform(x, y));
            star.RenderTransform = group;
            c.Children.Add(star);
            double seconds = 1.1 + random.NextDouble() * 1.6;
            double delay = random.NextDouble() * 2;
            loops.Swing(star, UIElement.OpacityProperty, 0.15, 1, seconds, delay);
            loops.Swing(pulse, ScaleTransform.ScaleXProperty, 0.55, 1.15, seconds, delay);
            loops.Swing(pulse, ScaleTransform.ScaleYProperty, 0.55, 1.15, seconds, delay);
        }
    }

    /// <summary>A cloud centred at <paramref name="centerX"/> with its flat base at <paramref name="baseY"/>, drifting sideways.</summary>
    private static void AddCloud(Canvas c, double centerX, double baseY, double scale, CloudTone tone, LoopSet loops,
        double drift, double seconds, double opacity = 1)
    {
        var (top, bottom) = tone switch
        {
            CloudTone.Light => (Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xC6, 0xD0, 0xDD)),
            CloudTone.Grey => (Color.FromRgb(0xD8, 0xDE, 0xE8), Color.FromRgb(0x96, 0xA2, 0xB2)),
            _ => (Color.FromRgb(0x86, 0x90, 0xA2), Color.FromRgb(0x4E, 0x56, 0x66))
        };
        var fill = new LinearGradientBrush(top, bottom, 90);
        fill.Freeze();
        var cloud = new Path
        {
            Data = WeatherIcons.Cloud,
            Fill = fill,
            Opacity = opacity,
            RenderTransform = new ScaleTransform(scale, scale)
        };
        var holder = new Canvas();
        holder.Children.Add(cloud);
        // The grid cloud is centred at x 10.25 and sits on y 16.
        Canvas.SetLeft(holder, centerX - 10.25 * scale);
        Canvas.SetTop(holder, baseY - 16 * scale);
        var move = new TranslateTransform();
        holder.RenderTransform = move;
        c.Children.Add(holder);
        loops.Swing(move, TranslateTransform.XProperty, -drift, drift, seconds);
        loops.Swing(move, TranslateTransform.YProperty, -1.2, 1.2, seconds * 0.7, seconds * 0.2);
    }

    private static void AddDrops(Canvas c, Random random, int count, double minSeconds, double maxSeconds,
        double width, double length, bool splash, LoopSet loops)
    {
        var dropBrush = new LinearGradientBrush(Color.FromArgb(0x00, 0x8C, 0xCB, 0xFF), Color.FromArgb(0xFF, 0x8C, 0xCB, 0xFF), 90);
        dropBrush.Freeze();
        var splashBrush = WeatherIcons.Brush(Color.FromArgb(0xCC, 0x8C, 0xCB, 0xFF));
        const double fall = 34, slant = 14;
        double dx = -fall * Math.Tan(slant * Math.PI / 180);
        for (int i = 0; i < count; i++)
        {
            double x = 22 + (i * 37 % 58) + random.NextDouble() * 3;
            double seconds = minSeconds + random.NextDouble() * (maxSeconds - minSeconds);
            double delay = random.NextDouble() * seconds;
            var drop = new Rectangle { Width = width, Height = length, RadiusX = width / 2, RadiusY = width / 2, Fill = dropBrush, Opacity = 0 };
            Canvas.SetLeft(drop, x);
            Canvas.SetTop(drop, 60);
            var move = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(new RotateTransform(slant, width / 2, length / 2));
            group.Children.Add(move);
            drop.RenderTransform = group;
            c.Children.Insert(Math.Min(1, c.Children.Count), drop); // behind the clouds, above the glow
            loops.Run(move, TranslateTransform.YProperty, new DoubleAnimation(0, fall, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) });
            loops.Run(move, TranslateTransform.XProperty, new DoubleAnimation(0, dx, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) });
            loops.Run(drop, UIElement.OpacityProperty, FadeThrough(seconds, delay, 0.95, 0.12, 0.8));

            if (!splash || i % 2 == 1) continue;
            // A ring opens where the drop lands, timed to its fall.
            var ring = new Ellipse { Width = 8, Height = 2.6, Stroke = splashBrush, StrokeThickness = 0.8, Opacity = 0 };
            Canvas.SetLeft(ring, x + dx - 3);
            Canvas.SetTop(ring, 60 + fall + length - 1);
            var open = new ScaleTransform(0.2, 0.2, 4, 1.3);
            ring.RenderTransform = open;
            c.Children.Add(ring);
            var grow = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) };
            grow.KeyFrames.Add(new LinearDoubleKeyFrame(0.2, KeyTime.FromPercent(0)));
            grow.KeyFrames.Add(new LinearDoubleKeyFrame(0.2, KeyTime.FromPercent(0.86)));
            grow.KeyFrames.Add(new EasingDoubleKeyFrame(1.5, KeyTime.FromPercent(1), WeatherMotion.Out));
            loops.Run(open, ScaleTransform.ScaleXProperty, grow);
            loops.Run(open, ScaleTransform.ScaleYProperty, grow.Clone());
            var flash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) };
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0.85)));
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromPercent(0.88)));
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            loops.Run(ring, UIElement.OpacityProperty, flash);
        }
    }

    private static readonly Geometry FlakeShape = CreateFlakeShape();

    private static Geometry CreateFlakeShape()
    {
        var group = new GeometryGroup();
        for (int i = 0; i < 3; i++)
        {
            double a = i * Math.PI / 3;
            group.Children.Add(new LineGeometry(new Point(-Math.Cos(a), -Math.Sin(a)), new Point(Math.Cos(a), Math.Sin(a))));
        }
        var widened = group.GetWidenedPathGeometry(new Pen(Brushes.White, 0.32) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        widened.Freeze();
        return widened;
    }

    private static void AddFlakes(Canvas c, Random random, int count, LoopSet loops)
    {
        for (int i = 0; i < count; i++)
        {
            double x = 20 + (i * 41 % 60) + random.NextDouble() * 4;
            double size = 2.2 + random.NextDouble() * 1.8;
            bool star = i % 3 != 0;
            Shape flake = star
                ? new Path { Data = FlakeShape, Fill = Brushes.White, RenderTransform = new ScaleTransform(size, size) }
                : new Ellipse { Width = size * 1.4, Height = size * 1.4, Fill = Brushes.White, Margin = new Thickness(-size * 0.7, -size * 0.7, 0, 0) };
            var holder = new Canvas { Opacity = 0 };
            holder.Children.Add(flake);
            Canvas.SetLeft(holder, x);
            Canvas.SetTop(holder, 58);
            var spin = new RotateTransform(0);
            var sway = new TranslateTransform();
            var fall = new TranslateTransform();
            var group = new TransformGroup();
            group.Children.Add(spin);
            group.Children.Add(sway);
            group.Children.Add(fall);
            holder.RenderTransform = group;
            c.Children.Add(holder);
            double seconds = 2.8 + random.NextDouble() * 1.6;
            double delay = random.NextDouble() * seconds;
            loops.Run(fall, TranslateTransform.YProperty, new DoubleAnimation(0, 40, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) });
            loops.Swing(sway, TranslateTransform.XProperty, -4.5, 4.5, 1.1 + random.NextDouble() * 0.7, delay);
            if (star)
                loops.Run(spin, RotateTransform.AngleProperty, new DoubleAnimation(0, i % 2 == 0 ? 180 : -180, TimeSpan.FromSeconds(4 + random.NextDouble() * 3)) { RepeatBehavior = RepeatBehavior.Forever });
            loops.Run(holder, UIElement.OpacityProperty, FadeThrough(seconds, delay, 0.95, 0.15, 0.75));
        }
    }

    private static DoubleAnimationUsingKeyFrames StrikeFrames(double peak)
    {
        // Two quick strikes, then a long calm; the same rhythm for the bolt and the sky.
        var frames = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(4.6), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(0.6) };
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(peak, KeyTime.FromPercent(0.30)));
        frames.KeyFrames.Add(new LinearDoubleKeyFrame(peak * 0.15, KeyTime.FromPercent(0.33)));
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(peak, KeyTime.FromPercent(0.35)));
        frames.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(0.47), WeatherMotion.Out));
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        return frames;
    }

    private static void AddFlash(Canvas c, LoopSet loops)
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x90, 0xE8, 0xE0, 0xFF), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xE8, 0xE0, 0xFF), 1));
        brush.Freeze();
        var flash = new Ellipse { Width = 130, Height = 120, Fill = brush, Opacity = 0, IsHitTestVisible = false };
        Canvas.SetLeft(flash, -15);
        Canvas.SetTop(flash, -20);
        c.Children.Add(flash);
        loops.Run(flash, UIElement.OpacityProperty, StrikeFrames(1));
    }

    private static void AddBolt(Canvas c, LoopSet loops)
    {
        var holder = new Canvas { Opacity = 0 };
        var transform = new MatrixTransform(4, 0, 0, 4, 2, 12);
        holder.Children.Add(new Path
        {
            Data = WeatherIcons.Bolt,
            Stroke = WeatherIcons.Brush(Color.FromArgb(0x60, 0xFF, 0xD6, 0x0A)),
            StrokeThickness = 1.4,
            StrokeLineJoin = PenLineJoin.Round,
            RenderTransform = transform
        });
        holder.Children.Add(new Path { Data = WeatherIcons.Bolt, Fill = WeatherIcons.Brush(WeatherIcons.BoltColor), RenderTransform = transform });
        c.Children.Add(holder);
        loops.Run(holder, UIElement.OpacityProperty, StrikeFrames(1));
    }

    private static void AddFog(Canvas c, LoopSet loops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xD0, 0xD6, 0xE0), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xDD, 0xD0, 0xD6, 0xE0), 0.3));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xDD, 0xD0, 0xD6, 0xE0), 0.7));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xD0, 0xD6, 0xE0), 1));
        brush.Freeze();
        (double X, double Y, double W, double Seconds)[] banks = { (14, 60, 66, 4.4), (26, 70, 56, 5.6), (10, 80, 72, 4.9), (30, 90, 48, 6.2) };
        for (int i = 0; i < banks.Length; i++)
        {
            var (x, y, w, seconds) = banks[i];
            var bank = new Rectangle { Width = w, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Fill = brush };
            Canvas.SetLeft(bank, x);
            Canvas.SetTop(bank, y);
            var move = new TranslateTransform();
            bank.RenderTransform = move;
            c.Children.Add(bank);
            double reach = i % 2 == 0 ? 10 : -10;
            loops.Swing(move, TranslateTransform.XProperty, -reach, reach, seconds);
            loops.Swing(bank, UIElement.OpacityProperty, 0.45, 0.95, seconds * 0.8, i * 0.4);
        }
    }

    private static void AddWind(Canvas c, LoopSet loops)
    {
        string[] gusts =
        {
            "M-6,30 C14,24 34,36 58,28 S88,22 104,30",
            "M-4,54 C20,48 40,60 66,52 S92,46 106,54",
            "M2,80 C22,74 44,86 70,78"
        };
        var stroke = WeatherIcons.Brush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF));
        for (int i = 0; i < gusts.Length; i++)
        {
            var gust = new Path
            {
                Data = Geometry.Parse(gusts[i]),
                Stroke = stroke,
                StrokeThickness = 1.3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeDashCap = PenLineCap.Round,
                StrokeDashArray = new DoubleCollection { 14, 90 },
                IsHitTestVisible = false
            };
            c.Children.Add(gust);
            // The dash runs along the curve like a gust passing through.
            loops.Run(gust, Shape.StrokeDashOffsetProperty, new DoubleAnimation(104, 0, TimeSpan.FromSeconds(1.9 + i * 0.35))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(i * 0.55),
                EasingFunction = WeatherMotion.Sine
            });
        }
    }
}
