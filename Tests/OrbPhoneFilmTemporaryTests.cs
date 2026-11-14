using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VNotch.Models;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class OrbPhoneFilmTemporaryTests
{
    [Fact]
    public void FilmOrbAndPhone() => SharedStaTestRunner.RunAsync(async ct =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "vnotch-film-pills-orb-phone");
        Directory.CreateDirectory(directory);
        string round = Environment.GetEnvironmentVariable("VNOTCH_FILM_ROUND") ?? "1";
        await using var fixture = OrbPhoneMotionTests.Create();
        var window = fixture.Window;
        window.Left = -20000;
        window.Top = -20000;
        window.Show();
        await WpfFrameWaiter.UntilAsync(() => window.CollapsedContent.IsVisible, "closed notch", ct);
        OrbPhoneMotionTests.Invoke(window, "SetBubbleFace", LiveActivity.Countdown);
        OrbPhoneMotionTests.Set(window, "_bubbleShown", true);
        OrbPhoneMotionTests.Invoke(window, "BudBubble");
        await Film(window.NotchContainer, Path.Combine(directory, $"orb-bud-{round}.png"), 650, 50, ct);
        OrbPhoneMotionTests.Invoke(window, "OpenBubble");
        await Film(window.NotchContainer, Path.Combine(directory, $"orb-open-{round}.png"), 650, 50, ct);
        OrbPhoneMotionTests.Set(window, "_bubbleShown", false);
        OrbPhoneMotionTests.Invoke(window, "RetractBubble", false);
        await Film(window.NotchContainer, Path.Combine(directory, $"orb-merge-{round}.png"), 650, 50, ct);
        OrbPhoneMotionTests.Set(window, "_bubbleShown", true);
        OrbPhoneMotionTests.Invoke(window, "BudBubble");
        await Film(window.NotchContainer, Path.Combine(directory, $"orb-reverse-{round}.png"), 650, 50, ct,
            () => { OrbPhoneMotionTests.Set(window, "_bubbleShown", false); OrbPhoneMotionTests.Invoke(window, "RetractBubble", false); });

        var phone = new PhoneMirrorWindow(() => null);
        try
        {
            phone.Left = -20000;
            phone.Top = -20000;
            phone.Width = 80;
            phone.Height = 20;
            var slot = OrbPhoneMotionTests.Get<Grid>(phone, "_screenSlot");
            slot.Background = new SolidColorBrush(Color.FromRgb(30, 34, 44));
            slot.Children.Add(new TextBlock { Text = "12:34\n\nPhone preview", Foreground = Brushes.White, FontSize = 24,
                TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            phone.Show();
            var start = new Int32Rect(-20000, -20000, 80, 20);
            var target = new Int32Rect(-20050, -20000, 180, 360);
            OrbPhoneMotionTests.Invoke(phone, "Spring", start, target, Motion.Pop, null);
            await Film((FrameworkElement)phone.Content, Path.Combine(directory, $"phone-grow-{round}.png"), 650, 50, ct, phone: true);
            OrbPhoneMotionTests.Invoke(phone, "Spring", target, start, Motion.PopBack, null);
            await Film((FrameworkElement)phone.Content, Path.Combine(directory, $"phone-pull-{round}.png"), 650, 50, ct, phone: true);
            OrbPhoneMotionTests.Invoke(phone, "Spring", start, target, Motion.Pop, null);
            await Film((FrameworkElement)phone.Content, Path.Combine(directory, $"phone-reverse-{round}.png"), 650, 50, ct,
                () => OrbPhoneMotionTests.Invoke(phone, "Spring", start, start, Motion.PopBack, null), phone: true);
        }
        finally { phone.Close(); }
    }, timeoutSeconds: 90);

    private static async Task Film(FrameworkElement visual, string path, int length, int step,
        CancellationToken ct, Action? reverse = null, bool phone = false)
    {
        const int width = 320, height = 210, columns = 7;
        int count = length / step + 1;
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            var clock = Stopwatch.StartNew();
            bool reversed = false;
            for (int i = 0; i < count; i++)
            {
                int wait = i * step - (int)clock.ElapsedMilliseconds;
                if (wait > 0) await Task.Delay(wait, ct);
                if (reverse != null && !reversed && clock.ElapsedMilliseconds >= 100) { reversed = true; reverse(); }
                visual.UpdateLayout();
                double w = Math.Max(1, visual.ActualWidth), h = Math.Max(1, visual.ActualHeight);
                var frame = new RenderTargetBitmap((int)Math.Ceiling(w * 2), (int)Math.Ceiling(h * 2), 192, 192, PixelFormats.Pbgra32);
                frame.Render(visual);
                int x = i % columns * width, y = i / columns * height;
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(61, 64, 72)), null, new Rect(x, y, width, height));
                double scale = phone ? 0.46 : Math.Min(0.45, width / w);
                dc.DrawImage(frame, new Rect(x + (width - w * scale) / 2, y + 24, w * scale, h * scale));
                dc.DrawText(new FormattedText($"{clock.ElapsedMilliseconds} ms", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, 2), new Point(x + 6, y + 3));
            }
        }
        var sheet = new RenderTargetBitmap(width * columns * 2, height * ((count + columns - 1) / columns) * 2, 192, 192, PixelFormats.Pbgra32);
        sheet.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sheet));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
