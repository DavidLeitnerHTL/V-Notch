using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using VNotch.WinUI.Motion;
using Windows.Storage.Streams;
using Windows.UI;
using static VNotch.WinUI.Composition.Anim;

namespace VNotch.WinUI.Composition;

/// <summary>
/// The blurred cover behind the media page (WPF MediaBackground and MediaBackground2): two stretched copies of the
/// blurred cover, each faded out by a radial mask around the cover side, with a black dim on top for bright art.
/// Positions are relative to the media page's top-left corner.
/// </summary>
internal sealed class MediaBackdrop
{
    private readonly Compositor _c;
    private readonly CompositionSurfaceBrush _image;
    private readonly CompositionColorBrush _dim;
    private LoadedImageSurface? _surface;
    private float _opacity;

    public ContainerVisual Visual { get; }

    public MediaBackdrop(Compositor c)
    {
        _c = c;
        _image = c.CreateSurfaceBrush();
        _image.Stretch = CompositionStretch.Fill;
        _dim = c.CreateColorBrush(Color.FromArgb(0, 0, 0, 0));
        Visual = c.CreateContainerVisual();
        Visual.Opacity = 0;
        // ExpandedContent sits at (8, 2) in the page; the layers' margins are (-40,-26,-36,-48) and (-16,-14,-18,-18).
        AddLayer(new Vector2(8 - 40, 2 - 26), new Vector2(464 + 40 + 36, 137 + 26 + 48),
            new Vector2(0.30f, 0.56f), 0.52f, new byte[] { 0xFF, 0x99, 0x44, 0x00 });
        AddLayer(new Vector2(8 - 16, 2 - 14), new Vector2(464 + 16 + 18, 137 + 14 + 18),
            new Vector2(0.22f, 0.36f), 0.46f, new byte[] { 0xDD, 0x77, 0x33, 0x00 });
    }

    private void AddLayer(Vector2 offset, Vector2 size, Vector2 centre, float radius, byte[] alphas)
    {
        var mask = _c.CreateRadialGradientBrush();
        mask.EllipseCenter = centre;
        mask.EllipseRadius = new Vector2(radius, radius);
        float[] offsets = { 0, 0.35f, 0.65f, 0.95f };
        for (int i = 0; i < offsets.Length; i++)
            mask.ColorStops.Add(_c.CreateColorGradientStop(offsets[i], Color.FromArgb(alphas[i], 255, 255, 255)));

        foreach (var source in new CompositionBrush[] { _image, _dim })
        {
            var masked = _c.CreateMaskBrush();
            masked.Source = source;
            masked.Mask = mask;
            var sprite = _c.CreateSpriteVisual();
            sprite.Brush = masked;
            sprite.Offset = new Vector3(offset, 0);
            sprite.Size = size;
            Visual.Children.InsertAtTop(sprite);
        }
    }

    /// <summary>A new cover: swap the image at once (the page is hidden or fading anyway) and keep its opacity.</summary>
    public void SetLook(IRandomAccessStream blurred, double dim, double opacity)
    {
        _surface?.Dispose();
        _surface = LoadedImageSurface.StartLoadFromStream(blurred);
        _image.Surface = _surface;
        _dim.Color = Color.FromArgb((byte)Math.Round(Math.Clamp(dim, 0, 1) * 255), 0, 0, 0);
        _opacity = (float)opacity;
    }

    public void Clear()
    {
        _surface?.Dispose();
        _surface = null;
        _image.Surface = null;
        _opacity = 0;
    }

    /// <summary>WPF fades the backdrop in once the notch has opened: 500 ms on QuadOut.</summary>
    public void FadeIn() => Run(Visual, "Opacity", null, _opacity, 500, Easings.QuadOut);

    /// <summary>WPF HideMediaBackgroundOverlay: 400 ms on PowerIn2.</summary>
    public void FadeOut() => Run(Visual, "Opacity", null, 0, 400, Easings.PowerIn2);
}
