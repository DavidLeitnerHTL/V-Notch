using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VNotch.Services;
using VNotch.Services.ClaudeCode;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class ClaudeDetailFilmTests
{
    [Fact]
    public void FilmClaudeDetails() => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool previous = AnimationConfig.ReduceMotion;
        AnimationConfig.SetReduceMotion(false);
        string directory = Path.Combine(Path.GetTempPath(), "vnotch-film-detail", "claude");
        Directory.CreateDirectory(directory);
        int round = Directory.GetFiles(directory, "limits-*.png").Length + 1;
        try
        {
            using var fixture = new ClaudeDetailMotionTests.Fixture(72);
            await Capture(fixture, Path.Combine(directory, $"limits-{round}.png"), _ => { }, ct);
            await ClaudeDetailMotionTests.Settled(ClaudeDetailMotionTests.Field<ScaleTransform>(fixture.Bar, "Grow"),
                ClaudeDetailMotionTests.Field<VNotch.Controls.WeatherTween>(fixture.Bar, "Number"), ct);
            fixture.SetUsage(15);
            await Capture(fixture, Path.Combine(directory, $"interruption-{round}.png"), index =>
            {
                if (index == 2) fixture.SetUsage(92);
                if (index == 5) fixture.SetUsage(32);
            }, ct);

            var session = new ClaudeSession
            {
                Id = "film-session", Title = "V-Notch", Cwd = @"C:\fake\V-Notch", Status = ClaudeSessionStatus.Working,
                Activity = "Reading ControlCenterView.cs", TurnStartedUtc = DateTime.UtcNow.AddMinutes(-2)
            };
            void Hero()
            {
                var hero = (FrameworkElement)ClaudeDetailMotionTests.Invoke(fixture.View, "Hero", session, new List<ClaudePermissionRequest>())!;
                fixture.Section.Children.RemoveAt(0);
                fixture.Section.Children.Insert(0, hero);
                fixture.Window.UpdateLayout();
            }
            Hero();
            await Capture(fixture, Path.Combine(directory, $"state-{round}.png"), index =>
            {
                if (index == 1) { session.Activity = "Running focused tests"; Hero(); }
                if (index == 3) { session.Status = ClaudeSessionStatus.Done; session.Summary = "Focused checks passed"; session.FinishedUtc = DateTime.UtcNow; Hero(); }
                if (index == 5) { session.Status = ClaudeSessionStatus.Working; session.Activity = "Checking the last frame"; Hero(); }
            }, ct);
        }
        finally { AnimationConfig.SetReduceMotion(previous); }
    });

    private static async Task Capture(ClaudeDetailMotionTests.Fixture fixture, string path, Action<int> trigger, CancellationToken ct)
    {
        const int count = 14, columns = 4, cellWidth = 620, cellHeight = 300;
        var sheet = new DrawingVisual();
        using (var drawing = sheet.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(23, 23, 25)), null, new Rect(0, 0, columns * cellWidth, 4 * cellHeight));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int index = 0; index < count; index++)
            {
                trigger(index);
                fixture.Window.UpdateLayout();
                var visual = (FrameworkElement)fixture.Window.Content;
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * 2),
                    (int)Math.Ceiling(visual.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                int x = index % columns * cellWidth, y = index / columns * cellHeight;
                drawing.DrawText(new FormattedText($"{clock.ElapsedMilliseconds} ms", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, 1), new Point(x + 8, y + 4));
                drawing.DrawImage(bitmap, new Rect(x, y + 24, cellWidth, 265));
                await Task.Delay(50, ct);
            }
        }
        var output = new RenderTargetBitmap(columns * cellWidth, 4 * cellHeight, 96, 96, PixelFormats.Pbgra32);
        output.Render(sheet);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
