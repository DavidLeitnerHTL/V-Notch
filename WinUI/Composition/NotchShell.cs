using System.Numerics;
using Microsoft.UI.Composition;
using VNotch.WinUI.Motion;
using Windows.UI;
using static VNotch.WinUI.Composition.Anim;
using static VNotch.WinUI.Motion.ShadowFalloff;

namespace VNotch.WinUI.Composition;

/// <summary>
/// The notch's shape on the compositor: the black body, the hover glow and the painted shadow. Everything is
/// bound by expressions to one property set, so animating S (size, Vector2) and R (bottom corner radius) moves
/// all of it together on the GPU, at the monitor's rate.
/// </summary>
internal sealed class NotchShell
{
    private readonly Compositor _c;
    private readonly string _w;

    public CompositionPropertySet Props { get; }
    public ContainerVisual Shadow { get; }
    public ShapeVisual Body { get; }
    public ShapeVisual Glow { get; }

    public NotchShell(Compositor c, float windowWidth, float windowHeight, Vector2 size, float radius)
    {
        _c = c;
        _w = F(windowWidth);
        Props = c.CreatePropertySet();
        Props.InsertVector2("S", size);
        Props.InsertScalar("R", radius);

        var area = new Vector2(windowWidth, windowHeight);
        Body = c.CreateShapeVisual();
        Body.Size = area;
        var body = c.CreateSpriteShape(CreateGeometry());
        body.FillBrush = c.CreateColorBrush(Color.FromArgb(255, 0, 0, 0));
        Body.Shapes.Add(body);

        Glow = c.CreateShapeVisual();
        Glow.Size = area;
        Glow.Opacity = 0;
        Glow.Shapes.Add(CreateGlowShape());

        Shadow = c.CreateContainerVisual();
        Shadow.Size = area;
        BuildShadow();
    }

    /// <summary>
    /// The notch as a rounded rectangle pushed up by its radius, so only the bottom corners round. An inset
    /// shrinks it on every side, for a hairline stroke that stays inside the edge.
    /// </summary>
    public CompositionRoundedRectangleGeometry CreateGeometry(float inset = 0)
    {
        var g = _c.CreateRoundedRectangleGeometry();
        string i = F(inset);
        Bind(g, "Size", $"Vector2(p.S.X - 2 * {i}, p.S.Y + p.R - 2 * {i})", ("p", Props));
        Bind(g, "Offset", $"Vector2(({_w} - p.S.X) / 2 + {i}, -p.R + {i})", ("p", Props));
        Bind(g, "CornerRadius", $"Vector2(Max(p.R - {i}, 0), Max(p.R - {i}, 0))", ("p", Props));
        return g;
    }

    // The WPF HoverGlow: a soft light from the top centre plus a hairline that fades from the top-left.
    private CompositionSpriteShape CreateGlowShape()
    {
        var shape = _c.CreateSpriteShape(CreateGeometry(0.5f));
        string left = $"(({_w} - p.S.X) / 2)";

        var fill = _c.CreateRadialGradientBrush();
        fill.MappingMode = CompositionMappingMode.Absolute;
        fill.ColorStops.Add(_c.CreateColorGradientStop(0, Color.FromArgb(0x28, 255, 255, 255)));
        fill.ColorStops.Add(_c.CreateColorGradientStop(0.65f, Color.FromArgb(0x08, 255, 255, 255)));
        fill.ColorStops.Add(_c.CreateColorGradientStop(1, Color.FromArgb(0, 255, 255, 255)));
        Bind(fill, "EllipseCenter", $"Vector2({_w} / 2, 0)", ("p", Props));
        Bind(fill, "EllipseRadius", "Vector2(Max(p.S.X * 0.9, 1), Max(p.S.Y * 1.1, 1))", ("p", Props));
        shape.FillBrush = fill;

        var stroke = _c.CreateLinearGradientBrush();
        stroke.MappingMode = CompositionMappingMode.Absolute;
        stroke.ColorStops.Add(_c.CreateColorGradientStop(0, Color.FromArgb(0x60, 255, 255, 255)));
        stroke.ColorStops.Add(_c.CreateColorGradientStop(0.45f, Color.FromArgb(0x18, 255, 255, 255)));
        stroke.ColorStops.Add(_c.CreateColorGradientStop(1, Color.FromArgb(0, 255, 255, 255)));
        Bind(stroke, "StartPoint", $"Vector2({left}, 0)", ("p", Props));
        Bind(stroke, "EndPoint", $"Vector2({left} + p.S.X * 0.9, p.S.Y)", ("p", Props));
        shape.StrokeBrush = stroke;
        shape.StrokeThickness = 1;
        return shape;
    }

    // Port of NotchSoftShadow: edge bands with linear gradients and corner pieces with radial ones. The top edge
    // sits off screen, so only the sides, the bottom and the four corners are drawn.
    private void BuildShadow()
    {
        string left = $"(({_w} - p.S.X) / 2)";
        string reach = F(Reach), depth = F(Depth), inset = F(Inset), band = F(Band);

        AddBand(new Vector2(0, 0.5f), new Vector2(1, 0.5f),
            $"Vector3({left} - {reach}, {depth}, 0)", $"Vector2({band}, Max(0, p.S.Y - p.R))");
        AddBand(new Vector2(1, 0.5f), new Vector2(0, 0.5f),
            $"Vector3({left} + p.S.X - {inset}, {depth}, 0)", $"Vector2({band}, Max(0, p.S.Y - p.R))");
        AddBand(new Vector2(0.5f, 1), new Vector2(0.5f, 0),
            $"Vector3({left} + p.R, p.S.Y + {depth} - {inset}, 0)", $"Vector2(Max(0, p.S.X - 2 * p.R), {band})");

        // Top corners are square (radius 0); only their lowest 4 DIP show beside the notch.
        AddCorner(new Vector2(1, 1), $"Vector3({left} - {reach}, {depth} - {reach}, 0)", $"Vector2({reach}, {reach})", roundCorner: false);
        AddCorner(new Vector2(0, 1), $"Vector3({left} + p.S.X, {depth} - {reach}, 0)", $"Vector2({reach}, {reach})", roundCorner: false);
        AddCorner(new Vector2(0, 0), $"Vector3({left} + p.S.X - p.R, p.S.Y + {depth} - p.R, 0)", $"Vector2(p.R + {reach}, p.R + {reach})", roundCorner: true);
        AddCorner(new Vector2(1, 0), $"Vector3({left} - {reach}, p.S.Y + {depth} - p.R, 0)", $"Vector2(p.R + {reach}, p.R + {reach})", roundCorner: true);
    }

    private void AddBand(Vector2 outer, Vector2 inner, string offset, string size)
    {
        var brush = _c.CreateLinearGradientBrush();
        brush.StartPoint = outer;
        brush.EndPoint = inner;
        for (int i = 0; i <= Stops; i++)
            brush.ColorStops.Add(_c.CreateColorGradientStop((float)i / Stops, Shade(AlphaAt(BandDistance(i)))));
        AddSprite(brush, offset, size);
    }

    /// <summary>
    /// A radial falloff around a corner's arc centre. For a rounded corner the stops sit at fixed distances from
    /// the arc, so their offsets along the gradient follow the animated radius.
    /// </summary>
    private void AddCorner(Vector2 centre, string offset, string size, bool roundCorner)
    {
        var brush = _c.CreateRadialGradientBrush();
        brush.EllipseCenter = centre;
        brush.EllipseRadius = Vector2.One;
        for (int i = 0; i <= Stops; i++)
        {
            if (roundCorner)
            {
                double distance = -Inset + (double)i / Stops * Band;
                var stop = _c.CreateColorGradientStop(0, Shade(AlphaAt(distance)));
                brush.ColorStops.Add(stop);
                Bind(stop, "Offset", $"Clamp(({F(distance)} + p.R) / (p.R + {F(Reach)}), 0, 1)", ("p", Props));
            }
            else
            {
                double o = (double)i / Stops;
                brush.ColorStops.Add(_c.CreateColorGradientStop((float)o, Shade(AlphaAt(o * Reach))));
            }
        }
        AddSprite(brush, offset, size);
    }

    private void AddSprite(CompositionBrush brush, string offset, string size)
    {
        var sprite = _c.CreateSpriteVisual();
        sprite.Brush = brush;
        Bind(sprite, "Offset", offset, ("p", Props));
        Bind(sprite, "Size", size, ("p", Props));
        Shadow.Children.InsertAtTop(sprite);
    }

    private static Color Shade(double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), 0, 0, 0);
}
