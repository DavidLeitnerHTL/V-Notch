using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class HomeDetailFilmTests
{
    [Theory]
    [InlineData("round1", false)]
    [InlineData("interruption1", true)]
    public void Film(string name, bool reverse) => SharedStaTestRunner.RunAsync(async ct =>
    {
        using var scene = new HomeDetailMotionTests.Scene();
        await WpfFrameWaiter.NextAsync(ct);
        var frames = new List<(BitmapSource Image, long Ms)>();
        const int width = 1360, height = 488;
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 17; i++)
        {
            if (i == 1) { scene.Light(true); scene.Update(26, 25, 68, 235, 2.1, 64); }
            if (reverse && i == 3) { scene.Light(false); scene.Update(20, 22, 43, 110, 1.6, 50); }
            if (!reverse && i == 10) scene.Light(false);
            var bitmap = new RenderTargetBitmap(width, height, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render((Visual)scene.Window.Content);
            bitmap.Freeze();
            frames.Add((bitmap, clock.ElapsedMilliseconds));
            await Task.Delay(50, ct);
        }
        const int columns = 6, label = 44;
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, columns * width, 3 * (height + label)));
            for (int i = 0; i < frames.Count; i++)
            {
                int x = i % columns * width, y = i / columns * (height + label);
                dc.DrawImage(frames[i].Image, new Rect(x, y + label, width, height));
                dc.DrawText(new FormattedText($"{frames[i].Ms} ms", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 25, Brushes.White, 1), new Point(x + 8, y + 6));
            }
        }
        var sheet = new RenderTargetBitmap(columns * width, 3 * (height + label), 96, 96, PixelFormats.Pbgra32);
        sheet.Render(drawing);
        var folder = Path.Combine(Path.GetTempPath(), "vnotch-film-detail", "home");
        Directory.CreateDirectory(folder);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sheet));
        using var stream = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
    }, 90);
}
