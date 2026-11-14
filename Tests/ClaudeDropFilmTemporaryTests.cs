using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class ClaudeDropFilmTemporaryTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void FilmAskAndDrop() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = new GreetingAcceptanceTests.MainWindowFixture("en", greeting: false,
            configureSettings: settings => { settings.EnableLocalOnlyMode = true; settings.DisableMouseLeaveAutoClose = true; },
            configureServices: services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
        var window = fixture.Window;
        window.Left = -20000;
        window.Show();
        var logo = new DrawingImage(new GeometryDrawing(Brushes.SlateGray, null,
            new RectangleGeometry(new Rect(0, 0, 14, 14), 3, 3)));
        Invoke(window, "ShowNotchPill", "Claude", "Waiting for permission", logo, false, new Func<bool>(() => true), null, null, null);
        await WpfFrameWaiter.UntilAsync(() => window.ClaudeNotification.Opacity > 0.99, "pill shown", ct);
        await Film(window, "ask", () => Invoke(window, "PlayAskSettled", true),
            () => Invoke(window, "PlayAskSettled", false), ct);
        Invoke(window, "ShowNotchPill", "Discord", "Alex: hi", logo, false, new Func<bool>(() => true), null, null, null);
        await WpfFrameWaiter.UntilAsync(() => window.ClaudeNotification.Opacity > 0.99, "pill shown", ct);
        await Film(window, "drop", () => Invoke(window, "PlayNotificationDrop", IntPtr.Zero),
            () => Invoke(window, "PlayNotificationDrop", IntPtr.Zero), ct);
    });

    private static async Task Film(MainWindow window, string name, Action trigger, Action interrupt, CancellationToken ct)
    {
        var frames = new List<(BitmapSource Image, int Time)>();
        double width = 470, height = 175;
        var position = window.NotchBorder.TranslatePoint(new Point(0, 0), window.NotchContainer);
        double left = position.X + window.NotchBorder.ActualWidth / 2 - width / 2;
        var clock = Stopwatch.StartNew();
        trigger();
        bool interrupted = false;
        for (int i = 0; i < 17; i++)
        {
            if (!interrupted && clock.ElapsedMilliseconds >= 100)
            {
                interrupted = true;
                interrupt();
            }
            var frame = new RenderTargetBitmap((int)width * 2, (int)height * 2, 192, 192, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(37, 39, 46)), null, new Rect(0, 0, width, height));
                dc.PushTransform(new TranslateTransform(-left, -position.Y + 10));
                dc.DrawRectangle(new VisualBrush(window.NotchContainer), null,
                    new Rect(0, 0, window.NotchContainer.ActualWidth, window.NotchContainer.ActualHeight));
                dc.Pop();
            }
            frame.Render(visual);
            frames.Add((frame, (int)clock.ElapsedMilliseconds));
            await Task.Delay(40, ct);
        }
        var sheetVisual = new DrawingVisual();
        using (var dc = sheetVisual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width * 6, (height + 24) * 3));
            for (int i = 0; i < frames.Count; i++)
            {
                double x = i % 6 * width, y = i / 6 * (height + 24);
                dc.DrawImage(frames[i].Image, new Rect(x, y + 24, width, height));
                dc.DrawText(new FormattedText($"{frames[i].Time} ms", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.Black, 1), new Point(x + 6, y + 2));
            }
        }
        var sheet = new RenderTargetBitmap((int)width * 12, (int)(height + 24) * 6, 192, 192, PixelFormats.Pbgra32);
        sheet.Render(sheetVisual);
        string directory = Path.Combine(Path.GetTempPath(), "vnotch-film-pills", "claude-drop");
        Directory.CreateDirectory(directory);
        string round = Environment.GetEnvironmentVariable("VNOTCH_FILM_ROUND") ?? "1";
        using var file = File.Create(Path.Combine(directory, $"{name}-round{round}.png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sheet));
        encoder.Save(file);
    }

    private static object? Invoke(MainWindow window, string name, params object?[] args) =>
        typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
}
