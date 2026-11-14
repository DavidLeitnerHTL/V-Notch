using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Controllers;
using VNotch.Services;
using VNotch.Services.ControlCenter;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class PillFilmTemporaryTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void FilmNotificationAndHandoff() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = new GreetingAcceptanceTests.MainWindowFixture("en", false,
            settings => { settings.EnableLocalOnlyMode = true; settings.DisableMouseLeaveAutoClose = true; },
            services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
        var window = fixture.Window;
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new BackgroundWindow { Left = -20000, Width = 720, Height = 260, Content = root };
        host.Show();
        host.UpdateLayout();
        bool previous = AnimationConfig.ReduceMotion;
        AnimationConfig.SetReduceMotion(false);
        try
        {
            Show(window, "Messages", "The download is ready.");
            Assert.Equal(Visibility.Visible, window.AppNotificationPill.Visibility);
            await Film(window, "notification-arrive", null, ct);
            Invoke(window, "ExpandAppPill");
            await Film(window, "notification-expand-reverse", () => Invoke(window, "CollapseAppPill"), ct);
            Invoke(window, "DismissAppPill");
            await Film(window, "notification-exit", null, ct);

            var arbiter = (CompactPillArbiter)typeof(MainWindow).GetField("_compactPillArbiter", Private)!.GetValue(window)!;
            arbiter.ForceClear();
            arbiter.TryAcquire(CompactPillSlot.Volume);
            window.VolumeIndicatorContainer.Visibility = Visibility.Visible;
            window.VolumeIndicatorContainer.Opacity = 1;
            Motion.Stop(window.NotchBorder, FrameworkElement.WidthProperty, 430);
            host.UpdateLayout();
            Show(window, "Calendar", "Your meeting starts soon.");
            await Film(window, "pill-a-to-pill-b", null, ct);
        }
        finally
        {
            host.Content = null;
            host.Close();
            window.Content = root;
            AnimationConfig.SetReduceMotion(previous);
        }
    });

    private static void Show(MainWindow window, string title, string body) => Invoke(window, "ShowAppNotificationPill",
        new WindowsNotification(1, "Test", "test.app", title, body, null, new[] { body, "Open the app to continue." }, null), null, 0);

    private static async Task Film(MainWindow window, string name, Action? interrupt, CancellationToken ct)
    {
        var frames = new List<BitmapSource>();
        for (int i = 0; i < 18; i++)
        {
            if (i == 2) interrupt?.Invoke();
            window.NotchBorder.UpdateLayout();
            var frame = new DrawingVisual();
            using (var dc = frame.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 34)), null, new Rect(0, 0, 500, 180));
                dc.DrawText(new FormattedText($"{i * 50} ms", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 12, Brushes.White, 2), new Point(10, 4));
                double width = window.NotchBorder.ActualWidth, height = window.NotchBorder.ActualHeight;
                dc.DrawRectangle(new VisualBrush(window.NotchBorder) { Stretch = Stretch.Fill }, null,
                    new Rect((500 - width) / 2, 28, width, height));
            }
            var bitmap = new RenderTargetBitmap(1000, 360, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(frame);
            bitmap.Freeze();
            frames.Add(bitmap);
            await Task.Delay(50, ct);
        }
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
            for (int i = 0; i < frames.Count; i++) dc.DrawImage(frames[i], new Rect(i % 6 * 500, i / 6 * 180, 500, 180));
        var contact = new RenderTargetBitmap(6000, 1080, 192, 192, PixelFormats.Pbgra32);
        contact.Render(sheet);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(contact));
        string directory = Path.Combine(Path.GetTempPath(), "vnotch-film-pills");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, $"{name}-round{Environment.GetEnvironmentVariable("VNOTCH_FILM_ROUND") ?? "1"}.png"));
        encoder.Save(stream);
    }

    private static object? Invoke(MainWindow window, string name, params object?[] args)
        => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
}
