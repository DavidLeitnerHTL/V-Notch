using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace VNotch.WinUI.Media;

/// <summary>What the media page takes from an album cover: its colours, how much to dim it, and the blurred backdrop.</summary>
internal sealed record ArtworkLook(Color Main, Color Accent, double BackdropOpacity, double Dim, IRandomAccessStream Blurred);

/// <summary>
/// Reads a cover the way the WPF notch does, with the same native code (VNotch.Native.dll): the palette from a
/// 36 px sample, the brightness dim from a 32 px sample, and the backdrop as a 128 px wide image box-blurred twice
/// with radius 8 and darkened to 96 % (FastBlurService).
/// </summary>
internal static partial class Artwork
{
    public static async Task<ArtworkLook?> AnalyzeAsync(IRandomAccessStream cover, double brightnessBoost)
    {
        try
        {
            var decoder = await BitmapDecoder.CreateAsync(cover);
            byte[] palettePixels = await PixelsAsync(decoder, 36, 36);
            byte[] dimPixels = await PixelsAsync(decoder, 32, 32);
            uint blurHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * (128.0 / decoder.PixelWidth)));
            byte[] blurPixels = await PixelsAsync(decoder, 128, blurHeight);

            var (main, accent) = Palette(palettePixels, 36, 36);
            double dim = Dim(dimPixels, 32 * 32);
            double luminance = (0.2126 * main.R + 0.7152 * main.G + 0.0722 * main.B) / 255.0;
            vn_dicolor_get_adaptive_blur_opacity(luminance, brightnessBoost, out double opacity);
            // WPF lifts the backdrop of very dark covers so they still read as coloured.
            if (opacity > 0 && luminance < 0.25) opacity = Math.Min(opacity * (1.0 + (0.25 - luminance) * 1.4), 0.95);

            Blur(blurPixels, 128, (int)blurHeight);
            var blurred = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, blurred);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, 128, blurHeight, 96, 96, blurPixels);
            await encoder.FlushAsync();
            blurred.Seek(0);
            return new ArtworkLook(main, accent, opacity, dim, blurred);
        }
        catch (Exception e)
        {
            App.Log(e);
            return null;
        }
    }

    /// <summary>WPF LiftDarkColor: a colour darker than 50 % value is scaled up to 55 % so it reads on black.</summary>
    public static Color Lift(Color c)
    {
        double v = Math.Max(c.R, Math.Max(c.G, c.B)) / 255.0;
        if (v >= 0.50) return c;
        double scale = 0.55 / Math.Max(v, 0.01);
        return Color.FromArgb(255, (byte)Math.Clamp(c.R * scale, 0, 255), (byte)Math.Clamp(c.G * scale, 0, 255), (byte)Math.Clamp(c.B * scale, 0, 255));
    }

    private static async Task<byte[]> PixelsAsync(BitmapDecoder decoder, uint width, uint height)
    {
        var transform = new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant };
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        return data.DetachPixelData();
    }

    private static unsafe (Color Main, Color Accent) Palette(byte[] pixels, int width, int height)
    {
        VnPaletteResult r;
        fixed (byte* p = pixels) vn_dicolor_extract_advanced_palette(p, width, height, width * 4, out r);
        var fallback = (Color.FromArgb(255, 34, 34, 34), Color.FromArgb(255, 180, 180, 180));
        if (r.IsMonotone != 0 || r.PrimaryA == 0) return fallback;
        var main = Color.FromArgb(r.PrimaryA, r.PrimaryR, r.PrimaryG, r.PrimaryB);
        var accent = r.AccentA != 0 ? Color.FromArgb(r.AccentA, r.AccentR, r.AccentG, r.AccentB)
            : r.SecondaryA != 0 ? Color.FromArgb(r.SecondaryA, r.SecondaryR, r.SecondaryG, r.SecondaryB)
            : main;
        return (main, accent);
    }

    private static unsafe double Dim(byte[] pixels, int pixelCount)
    {
        double dim;
        fixed (byte* p = pixels) vn_dicolor_compute_brightness_dim(p, pixels.Length, pixelCount, out dim);
        return dim;
    }

    private static unsafe void Blur(byte[] pixels, int width, int height)
    {
        var scratch = new byte[pixels.Length];
        fixed (byte* p = pixels)
        fixed (byte* t = scratch)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                vn_fast_blur_box_horizontal(p, t, width, height, 8);
                vn_fast_blur_box_vertical(t, p, width, height, 8);
            }
            vn_fast_blur_darken_pixels(p, pixels.Length, 0.96f);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct VnPaletteResult
    {
        public byte PrimaryR, PrimaryG, PrimaryB, PrimaryA;
        public byte SecondaryR, SecondaryG, SecondaryB, SecondaryA;
        public byte AccentR, AccentG, AccentB, AccentA;
        public byte IsMonotone, IsFlatBg;
        public byte TextR, TextG, TextB, TextA;
    }

    [LibraryImport("VNotch.Native.dll")] private static unsafe partial int vn_dicolor_extract_advanced_palette(byte* pixels, int width, int height, int stride, out VnPaletteResult result);
    [LibraryImport("VNotch.Native.dll")] private static unsafe partial int vn_dicolor_compute_brightness_dim(byte* pixels, int bufLen, int pixelCount, out double dim);
    [LibraryImport("VNotch.Native.dll")] private static partial int vn_dicolor_get_adaptive_blur_opacity(double luminance, double brightnessBoost, out double opacity);
    [LibraryImport("VNotch.Native.dll")] private static unsafe partial int vn_fast_blur_box_horizontal(byte* source, byte* target, int w, int h, int radius);
    [LibraryImport("VNotch.Native.dll")] private static unsafe partial int vn_fast_blur_box_vertical(byte* source, byte* target, int w, int h, int radius);
    [LibraryImport("VNotch.Native.dll")] private static unsafe partial int vn_fast_blur_darken_pixels(byte* pixels, int length, float factor);
}
