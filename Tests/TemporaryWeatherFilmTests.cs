using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VNotch.Controls;
using VNotch.Models;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class TemporaryWeatherFilmTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Fact]
    public void WeatherContactSheets() => SharedStaTestRunner.RunAsync(async () =>
    {
        bool reduced = AnimationConfig.ReduceMotion;
        AnimationConfig.SetReduceMotion(false);
        AnimationConfig.Configure(60, false);
        using var view = new WeatherView();
        var forecast = (WeatherForecast)typeof(WeatherForecastTests).GetMethod("Forecast", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [DateTime.Now, null, null, 2, 14d, false, 19d])!;
        typeof(WeatherView).GetField("_forecast", Private)!.SetValue(view, forecast);
        view.Attach(_ => Task.FromResult<WeatherForecast?>(forecast), () => true);
        var window = new BackgroundWindow { Content = view, Width = 720, Height = 364, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false, Background = Brushes.Black };
        string round = Environment.GetEnvironmentVariable("WEATHER_FILM_ROUND") ?? "1";
        try
        {
            window.Show();
            view.UpdateLayout();
            typeof(WeatherView).GetMethod("Render", Private)!.Invoke(view, [true]);
            await Film("entrance", window, round);
            var rows = (System.Collections.IEnumerable)typeof(WeatherView).GetField("_dayRows", Private)!.GetValue(view)!;
            Border row = rows.Cast<object>().Select(item => (Border)item.GetType().GetField("Item2")!.GetValue(item)!).First();
            Raise(row, UIElement.PreviewMouseLeftButtonDownEvent);
            await Film("press-release-60ms", window, round, elapsed => { if (elapsed == 60) Raise(row, UIElement.PreviewMouseLeftButtonUpEvent); });
            var select = typeof(WeatherView).GetMethod("SelectDay", Private)!;
            select.Invoke(view, [DateTime.Today.AddDays(1)]);
            await Film("day-interruption-120ms", window, round, elapsed => { if (elapsed == 120) select.Invoke(view, [null]); });
            var updated = new WeatherForecast { Place = forecast.Place, Latitude = forecast.Latitude, Longitude = forecast.Longitude,
                FetchedAt = forecast.FetchedAt, Source = forecast.Source, Now = forecast.Now with { Temperature = 19, Humidity = 50, Code = 63 },
                Hours = forecast.Hours, Days = forecast.Days };
            typeof(WeatherView).GetField("_forecast", Private)!.SetValue(view, updated);
            typeof(WeatherView).GetMethod("Render", Private)!.Invoke(view, [false]);
            await Film("value-sky-change", window, round);
        }
        finally { window.Close(); AnimationConfig.SetReduceMotion(reduced); }
    });

    private static void Raise(UIElement element, RoutedEvent routedEvent) => element.RaiseEvent(
        new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routedEvent });

    private static async Task Film(string name, Window window, string round, Action<int>? action = null)
    {
        const int width = 720, height = 388, count = 12, columns = 4;
        var drawing = new DrawingVisual();
        var frames = new List<(BitmapSource Bitmap, int Time)>();
        for (int i = 0; i < count; i++)
        {
            if (i > 0) await Task.Delay(60);
            action?.Invoke(i * 60);
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width * 2, 364 * 2, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(window);
            frames.Add((bitmap, i * 60));
        }
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width * columns, height * 3));
            for (int i = 0; i < frames.Count; i++)
            {
                var origin = new Point(i % columns * width, i / columns * height);
                dc.DrawImage(frames[i].Bitmap, new Rect(origin, new Size(width, 364)));
                dc.DrawText(new FormattedText(frames[i].Time + " ms", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 14, Brushes.White, 2), new Point(origin.X + 8, origin.Y + 365));
            }
        }
        var sheet = new RenderTargetBitmap(width * columns * 2, height * 3 * 2, 192, 192, PixelFormats.Pbgra32);
        sheet.Render(drawing);
        string folder = Path.Combine(Path.GetTempPath(), "vnotch-film-tabs", "weather");
        Directory.CreateDirectory(folder);
        using var output = File.Create(Path.Combine(folder, $"round-{round}-{name}.png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sheet));
        encoder.Save(output);
    }
}
