using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using VNotch.WinUI.Motion;
using Windows.Storage.Streams;
using Windows.UI;
using static VNotch.WinUI.Composition.Anim;

namespace VNotch.WinUI.Composition;

/// <summary>Where the album art sits: top-left corner, side length and corner radius, in window DIP.</summary>
internal readonly record struct CoverRect(float X, float Y, float Size, float Radius);

/// <summary>
/// The album art as one compositor sprite. The same sprite is the small thumbnail in the closed notch and the
/// large cover in the media view, so opening flies it from one place to the other (the WPF notch's thumbnail
/// morph) without a hand-off between two images.
/// </summary>
internal sealed class CoverArt
{
    private readonly Compositor _c;
    private readonly SpriteVisual _sprite;
    private readonly CompositionPropertySet _p;
    private readonly CompositionColorBrush _placeholder;
    private LoadedImageSurface? _surface;

    public Visual Visual => _sprite;

    public CoverArt(Compositor c)
    {
        _c = c;
        _p = c.CreatePropertySet();
        _p.InsertScalar("X", 0);
        _p.InsertScalar("Y", 0);
        _p.InsertScalar("Size", 22);
        _p.InsertScalar("Radius", 6);

        _sprite = c.CreateSpriteVisual();
        _placeholder = c.CreateColorBrush(Color.FromArgb(0x26, 255, 255, 255));
        _sprite.Brush = _placeholder;
        _sprite.Opacity = 0;
        Bind(_sprite, "Offset", "Vector3(c.X, c.Y, 0)", ("c", _p));
        Bind(_sprite, "Size", "Vector2(c.Size, c.Size)", ("c", _p));
        Bind(_sprite, "CenterPoint", "Vector3(c.Size / 2, c.Size / 2, 0)", ("c", _p));

        var shape = c.CreateRoundedRectangleGeometry();
        Bind(shape, "Size", "Vector2(c.Size, c.Size)", ("c", _p));
        Bind(shape, "CornerRadius", "Vector2(c.Radius, c.Radius)", ("c", _p));
        _sprite.Clip = c.CreateGeometricClip(shape);
    }

    public void SetImage(IRandomAccessStream? stream)
    {
        _surface?.Dispose();
        _surface = null;
        if (stream == null)
        {
            _sprite.Brush = _placeholder;
            return;
        }
        _surface = LoadedImageSurface.StartLoadFromStream(stream);
        var brush = _c.CreateSurfaceBrush(_surface);
        brush.Stretch = CompositionStretch.UniformToFill;
        _sprite.Brush = brush;
    }

    public void Place(CoverRect r)
    {
        foreach (var name in new[] { "X", "Y", "Size", "Radius" }) _p.StopAnimation(name);
        _p.InsertScalar("X", r.X);
        _p.InsertScalar("Y", r.Y);
        _p.InsertScalar("Size", r.Size);
        _p.InsertScalar("Radius", r.Radius);
    }

    /// <summary>The WPF thumbnail morph: 500 ms on the soft spring curve, the corners on ExpOut6.</summary>
    public void MorphTo(CoverRect r, double delayMs = 0)
    {
        Run(_p, "X", null, r.X, 500, Easings.SoftSpring, delayMs);
        Run(_p, "Y", null, r.Y, 500, Easings.SoftSpring, delayMs);
        Run(_p, "Size", null, r.Size, 500, Easings.SoftSpring, delayMs);
        Run(_p, "Radius", null, r.Radius, 500, Easings.ExpOut6, delayMs);
    }

    public void Show(bool visible) { _sprite.StopAnimation("Opacity"); _sprite.Opacity = visible ? 1 : 0; }

    /// <summary>The media view's cover reveal when there was no thumbnail to fly in: fade 280 ms, grow from 84 %.</summary>
    public void Reveal(double delayMs)
    {
        Run(_sprite, "Opacity", 0, 1, 280, Easings.AppleOut, delayMs);
        Run(_sprite, "Scale.X", 0.84f, 1, 360, Easings.ExpOut6, delayMs);
        Run(_sprite, "Scale.Y", 0.84f, 1, 360, Easings.ExpOut6, delayMs);
    }

    public void FadeOut(double ms) => Run(_sprite, "Opacity", null, 0, ms, Easings.ExpOut6);

    public void ResetScale() { _sprite.StopAnimation("Scale.X"); _sprite.StopAnimation("Scale.Y"); _sprite.Scale = Vector3.One; }
}
