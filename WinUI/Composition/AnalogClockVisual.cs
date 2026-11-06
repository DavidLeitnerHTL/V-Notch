using System.Numerics;
using Microsoft.UI.Composition;
using Windows.UI;

namespace VNotch.WinUI.Composition;

/// <summary>
/// The WPF AnalogClock (Controls/AnalogClock.cs) as compositor shapes: ticks, hour markers, three hands and the
/// hub, with the same proportions. The hands sweep by compositor animations, so a running clock costs the app
/// nothing per frame; it only runs while the media page is open.
/// </summary>
internal sealed class AnalogClockVisual
{
    public const float Size = 92;
    private const float FacePadding = 4;
    // Sweep starts this long after the page opens, as in WPF (SweepStartDelayMs).
    private const double SweepDelayMs = 480;

    private static readonly Color Primary = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color MinorTick = Color.FromArgb(255, 0x6E, 0x6E, 0x73);
    public static readonly Color Accent = Color.FromArgb(255, 0xE0, 0x23, 0x1F);
    private static readonly Color HubHole = Color.FromArgb(255, 0x0A, 0x0A, 0x0A);

    private readonly Compositor _c;
    private readonly CompositionSpriteShape _hour, _minute, _second;

    public ShapeVisual Visual { get; }

    /// <summary>Face radius and centre in the clock's own coordinates; the date label is placed from these.</summary>
    public static float Radius => Size / 2 - FacePadding;
    public static Vector2 Centre => new(Size / 2, Size / 2);

    public AnalogClockVisual(Compositor c, bool showDate)
    {
        _c = c;
        Visual = c.CreateShapeVisual();
        Visual.Size = new Vector2(Size, Size);
        float r = Radius;
        var centre = Centre;

        var minor = c.CreateColorBrush(MinorTick);
        var major = c.CreateColorBrush(Primary);
        for (int i = 0; i < 60; i++)
        {
            bool five = i % 5 == 0;
            double angle = i * 6.0 * Math.PI / 180.0;
            var dir = new Vector2((float)Math.Sin(angle), -(float)Math.Cos(angle));
            var line = c.CreateLineGeometry();
            line.Start = centre + dir * (five ? r * 0.86f : r * 0.91f);
            line.End = centre + dir * (r * 0.97f);
            var tick = c.CreateSpriteShape(line);
            tick.StrokeBrush = five ? major : minor;
            tick.StrokeThickness = five ? 1.4f : 1.0f;
            tick.StrokeStartCap = tick.StrokeEndCap = CompositionStrokeCap.Round;
            Visual.Shapes.Add(tick);
        }

        float markerLength = r * 0.18f, markerThickness = Math.Max(2f, r * 0.075f), markerCentre = r * 0.70f;
        for (int h = 0; h < 12; h++)
        {
            if (showDate && h == 3) continue;
            var marker = Bar(new Vector2(markerThickness, markerLength), major);
            marker.Offset = new Vector2(centre.X - markerThickness / 2, centre.Y - (markerCentre + markerLength / 2));
            marker.CenterPoint = new Vector2(markerThickness / 2, markerCentre + markerLength / 2);
            marker.RotationAngleInDegrees = h * 30;
            Visual.Shapes.Add(marker);
        }

        _hour = Hand(r * 0.52f, r * 0.11f, Math.Max(2.5f, r * 0.085f), major);
        _minute = Hand(r * 0.74f, r * 0.13f, Math.Max(2.0f, r * 0.065f), major);
        _second = Hand(r * 0.82f, r * 0.22f, Math.Max(1.2f, r * 0.022f), c.CreateColorBrush(Accent));

        float hub = Math.Max(2.5f, r * 0.06f);
        Visual.Shapes.Add(Dot(centre, hub, c.CreateColorBrush(Accent)));
        Visual.Shapes.Add(Dot(centre, hub * 0.42f, c.CreateColorBrush(HubHole)));
        Show(DateTime.Now);
    }

    private CompositionSpriteShape Bar(Vector2 size, CompositionBrush brush)
    {
        var g = _c.CreateRoundedRectangleGeometry();
        g.Size = size;
        g.CornerRadius = new Vector2(size.X / 2, size.X / 2);
        var shape = _c.CreateSpriteShape(g);
        shape.FillBrush = brush;
        return shape;
    }

    private CompositionSpriteShape Hand(float length, float tail, float thickness, CompositionBrush brush)
    {
        var hand = Bar(new Vector2(thickness, length + tail), brush);
        hand.Offset = new Vector2(Centre.X - thickness / 2, Centre.Y - length);
        hand.CenterPoint = new Vector2(thickness / 2, length);
        Visual.Shapes.Add(hand);
        return hand;
    }

    private CompositionSpriteShape Dot(Vector2 centre, float radius, CompositionBrush brush)
    {
        var g = _c.CreateEllipseGeometry();
        g.Center = centre;
        g.Radius = new Vector2(radius, radius);
        var shape = _c.CreateSpriteShape(g);
        shape.FillBrush = brush;
        return shape;
    }

    private static (float Hour, float Minute, float Second) Angles(DateTime now)
    {
        double seconds = now.Second + now.Millisecond / 1000.0;
        double minutes = now.Minute + seconds / 60.0;
        double hours = now.Hour % 12 + minutes / 60.0;
        return ((float)(hours * 30), (float)(minutes * 6), (float)(seconds * 6));
    }

    /// <summary>Stops the sweep and shows the given time.</summary>
    public void Show(DateTime now)
    {
        var (h, m, s) = Angles(now);
        foreach (var hand in new[] { _hour, _minute, _second }) hand.StopAnimation("RotationAngleInDegrees");
        _hour.RotationAngleInDegrees = h;
        _minute.RotationAngleInDegrees = m;
        _second.RotationAngleInDegrees = s;
    }

    /// <summary>Shows the time, then sweeps all hands continuously on the compositor after the WPF start delay.</summary>
    public void Start()
    {
        var now = DateTime.Now;
        Show(now);
        var (h, m, s) = Angles(now.AddMilliseconds(SweepDelayMs));
        Sweep(_second, s, 60);
        Sweep(_minute, m, 3600);
        Sweep(_hour, h, 43200);
    }

    public void Stop() => Show(DateTime.Now);

    private void Sweep(CompositionSpriteShape hand, float from, double secondsPerTurn)
    {
        var a = _c.CreateScalarKeyFrameAnimation();
        var linear = _c.CreateLinearEasingFunction();
        a.InsertKeyFrame(0, from, linear);
        a.InsertKeyFrame(1, from + 360, linear);
        a.Duration = TimeSpan.FromSeconds(secondsPerTurn);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        a.DelayTime = TimeSpan.FromMilliseconds(SweepDelayMs);
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueAfterDelay;
        hand.StartAnimation("RotationAngleInDegrees", a);
    }
}
