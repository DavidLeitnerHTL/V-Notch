using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Services;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class HudFilmTemporaryTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void FilmHudAndMicReversal() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = new GreetingAcceptanceTests.MainWindowFixture("en", false,
            settings => { settings.EnableLocalOnlyMode = true; settings.DisableMouseLeaveAutoClose = true; },
            services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
        var window = fixture.Window;
        await window.CleanupAsync();
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Window { Left = -20000, Top = -20000, Width = 720, Height = 260,
            ShowActivated = false, ShowInTaskbar = false, Content = root, WindowStyle = WindowStyle.None };
        bool reduced = AnimationConfig.ReduceMotion;
        try
        {
            host.Show();
            host.UpdateLayout();
            Set(window, "_cleanedUp", false);
            AnimationConfig.SetReduceMotion(false);
            await Film(window, root, "volume-show-change-hide", i =>
            {
                if (i == 0 || i == 5) Invoke(window, "OnSystemLevelChanged", Kind("SystemVolume"), i == 0 ? .25f : .8f, false);
                if (i == 17) Invoke(window, "HideVolumeIndicator");
            }, ct);
            Invoke(window, "DismissVolumeIndicatorImmediate", false, false);
            Set(window, "_micMuteKnown", null);
            Invoke(window, "OnMicMuteChanged", false);
            await Film(window, root, "mic-double-toggle", i =>
            {
                if (i == 0) Invoke(window, "OnMicMuteChanged", true);
                if (i == 2) Invoke(window, "OnMicMuteChanged", false);
                if (i == 20) Invoke(window, "HideVolumeIndicator");
            }, ct);
        }
        finally
        {
            Set(window, "_cleanedUp", true);
            host.Content = null;
            host.Close();
            window.Content = root;
            AnimationConfig.SetReduceMotion(reduced);
        }
    });

    private static async Task Film(MainWindow window, FrameworkElement root, string name, Action<int> trigger, CancellationToken ct)
    {
        var sheet = new DrawingVisual();
        using (var dc = sheet.RenderOpen())
            for (int i = 0; i < 36; i++)
            {
                trigger(i);
                root.UpdateLayout();
                var frame = new RenderTargetBitmap(1440, 520, 192, 192, PixelFormats.Pbgra32);
                frame.Render(root);
                double x = i % 6 * 720, y = i / 6 * 160;
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(34, 34, 39)), null, new Rect(x, y, 720, 160));
                dc.DrawImage(frame, new Rect(x, y + 20, 720, 260));
                dc.DrawText(new FormattedText($"{i * 50} ms", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 12, Brushes.White, 2), new Point(x + 8, y + 2));
                await Task.Delay(50, ct);
            }
        var bitmap = new RenderTargetBitmap(8640, 1920, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(sheet);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string directory = Path.Combine(Path.GetTempPath(), "vnotch-film-pills");
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name + "-round" + (Environment.GetEnvironmentVariable("VNOTCH_FILM_ROUND") ?? "1") + ".png"));
        encoder.Save(stream);
    }

    private static object Kind(string name) => Enum.Parse(typeof(MainWindow).GetNestedType("LevelHudKind", BindingFlags.NonPublic)!, name);
    private static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
    private static void Invoke(MainWindow window, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
}
