using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests;

public sealed class SystemDetailFilmTests
{
    [Fact]
    public void Round1() => Film("round1");

    [Fact]
    public void Round2() => Film("round2");

    private static void Film(string round) => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool before = AnimationConfig.ReduceMotion;
        AnimationConfig.SetReduceMotion(false);
        using var rig = new SystemDetailMotionTests.Rig();
        try
        {
            await Capture(rig, round + "-arrival", null, ct);
            await rig.Settle(ct);
            rig.Output(.9f);
            rig.App(.9f);
            rig.Hover(rig.DisplayChip, true);
            rig.Hover(rig.OutputCard, true);
            await Capture(rig, round + "-interrupt", () =>
            {
                rig.Output(.16f, true);
                rig.App(.16f, true);
                rig.Hover(rig.DisplayChip, false);
                rig.Hover(rig.OutputCard, false);
            }, ct);
            await rig.Settle(ct);
            rig.Output(.54f);
            rig.App(.54f);
            await Capture(rig, round + "-release", null, ct);
        }
        finally { AnimationConfig.SetReduceMotion(before); }
    });

    private static async Task Capture(SystemDetailMotionTests.Rig rig, string name, Action? interrupt, CancellationToken ct)
    {
        int[] samples = [0, 50, 100, 150, 200, 300, 450, 700];
        var sheet = new DrawingVisual();
        using (var drawing = sheet.RenderOpen())
        {
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < samples.Length; i++)
            {
                int wait = samples[i] - (int)watch.ElapsedMilliseconds;
                if (wait > 0) await Task.Delay(wait, ct);
                if (samples[i] == 100) interrupt?.Invoke();
                rig.Surface.UpdateLayout();
                var bitmap = new RenderTargetBitmap(840, 540, 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(rig.Surface);
                drawing.DrawRectangle(Brushes.Black, null, new Rect(i * 420, 0, 420, 300));
                drawing.DrawImage(bitmap, new Rect(i * 420, 30, 420, 270));
                drawing.DrawText(new FormattedText($"{samples[i]} ms", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 15, Brushes.White, 2), new Point(i * 420 + 12, 4));
            }
        }
        var result = new RenderTargetBitmap(samples.Length * 840, 600, 192, 192, PixelFormats.Pbgra32);
        result.Render(sheet);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(result));
        string folder = Path.Combine(Path.GetTempPath(), "vnotch-film-detail", "system");
        Directory.CreateDirectory(folder);
        using var stream = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
    }
}
