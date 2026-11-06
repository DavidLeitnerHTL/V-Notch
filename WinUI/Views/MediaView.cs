using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using static VNotch.WinUI.Views.Look;

namespace VNotch.WinUI.Views;

/// <summary>
/// The open notch's media page, laid out like the WPF one at 480 × 147: tab row, cover space on the left,
/// title and artist, progress with times and the transport buttons, and the clock on the right. The cover itself
/// is a compositor sprite (CoverArt) so it can fly in from the closed notch.
/// </summary>
internal sealed class MediaView
{
    public const double Width = 480, Height = 147;
    // Cover rectangle inside the page, as measured from the WPF notch.
    public const float CoverX = 12, CoverY = 28, CoverSize = 102, CoverRadius = 14;

    private const double TextX = 126, PlaybackY = 86, PlaybackWidth = 166;

    private readonly TextBlock _title, _artist, _elapsed, _remaining, _time, _date, _battery;
    private readonly FontIcon _playGlyph;
    private readonly Border _progressFill;

    public Canvas Root { get; }
    /// <summary>Holds the blurred-cover backdrop, behind everything else on the page.</summary>
    public Grid BackdropHost { get; }
    public FrameworkElement Playback { get; }
    public FrameworkElement RightColumn { get; }

    public event Action? PreviousRequested, PlayPauseRequested, NextRequested;

    public MediaView()
    {
        Root = new Canvas { Width = Width, Height = Height, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        BackdropHost = new Grid { IsHitTestVisible = false };
        Root.Children.Add(BackdropHost);

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 15 };
        foreach (var glyph in new[] { "", "", "", "", "", "" })
            tabs.Children.Add(Icon(glyph, 12, Tertiary));
        Root.Children.Add(At(tabs, 24, 7));

        _battery = Text("", 10.5, Semibold, Secondary, FontWeights.SemiBold);
        Root.Children.Add(At(_battery, 404, 6));

        _title = Text("", 14, Semibold, White, FontWeights.SemiBold);
        _title.Width = 180;
        _artist = Text("", 11.5, Medium, Secondary, FontWeights.Medium);
        _artist.Width = 180;
        Root.Children.Add(At(_title, TextX, 31));
        Root.Children.Add(At(_artist, TextX, 50));

        var playback = new Canvas { Width = PlaybackWidth, Height = 52 };
        var track = new Border { Width = PlaybackWidth, Height = 4, CornerRadius = new CornerRadius(2), Background = Track };
        _progressFill = new Border { Width = 0, Height = 4, CornerRadius = new CornerRadius(2), Background = Fill, HorizontalAlignment = HorizontalAlignment.Left };
        var bar = new Grid { Width = PlaybackWidth, Height = 4 };
        bar.Children.Add(track);
        bar.Children.Add(_progressFill);
        playback.Children.Add(bar);
        _elapsed = Text("0:00", 9, Medium, Tertiary, FontWeights.Medium);
        _remaining = Text("0:00", 9, Medium, Tertiary, FontWeights.Medium);
        _remaining.Width = 60;
        _remaining.TextAlignment = TextAlignment.Right;
        playback.Children.Add(At(_elapsed, 0, 8));
        playback.Children.Add(At(_remaining, PlaybackWidth - 60, 8));
        _playGlyph = Icon("", 17, White);
        playback.Children.Add(At(Button(Icon("", 14, White), () => PreviousRequested?.Invoke()), PlaybackWidth / 2 - 62, 22));
        playback.Children.Add(At(Button(_playGlyph, () => PlayPauseRequested?.Invoke()), PlaybackWidth / 2 - 15, 20));
        playback.Children.Add(At(Button(Icon("", 14, White), () => NextRequested?.Invoke()), PlaybackWidth / 2 + 32, 22));
        Root.Children.Add(At(playback, TextX, PlaybackY));
        Playback = playback;

        var right = new StackPanel { Width = 150, Spacing = 0 };
        _date = Text("", 11.5, Semibold, Secondary, FontWeights.SemiBold);
        _date.HorizontalAlignment = HorizontalAlignment.Center;
        _time = Text("", 38, Regular, White, FontWeights.Normal);
        _time.HorizontalAlignment = HorizontalAlignment.Center;
        right.Children.Add(_date);
        right.Children.Add(_time);
        Root.Children.Add(At(right, 314, 44));
        RightColumn = right;
    }

    private static Grid Button(FontIcon icon, Action onClick)
    {
        var hit = new Grid { Width = 30, Height = 28, Background = Clear };
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        hit.Children.Add(icon);
        hit.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(hit).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            onClick();
        };
        return hit;
    }

    public void SetTrack(string title, string artist)
    {
        _title.Text = title;
        _artist.Text = artist;
    }

    /// <summary>
    /// WPF tints the playback row with the cover's lifted accent: the progress runs from the accent to 65 % of it,
    /// the times wear the accent. Without a cover the row stays white.
    /// </summary>
    public void SetAccent(Windows.UI.Color? accent)
    {
        if (accent is not { } c)
        {
            _progressFill.Background = Fill;
            _elapsed.Foreground = _remaining.Foreground = Tertiary;
            return;
        }
        var dark = Windows.UI.Color.FromArgb(c.A, (byte)(c.R * 0.65), (byte)(c.G * 0.65), (byte)(c.B * 0.65));
        _progressFill.Background = new Microsoft.UI.Xaml.Media.LinearGradientBrush(new Microsoft.UI.Xaml.Media.GradientStopCollection
        {
            new Microsoft.UI.Xaml.Media.GradientStop { Color = c, Offset = 0 },
            new Microsoft.UI.Xaml.Media.GradientStop { Color = dark, Offset = 1 },
        }, 0);
        _elapsed.Foreground = _remaining.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(c);
    }

    public void SetPlaying(bool playing) => _playGlyph.Glyph = playing ? "" : "";

    public void SetProgress(TimeSpan position, TimeSpan duration)
    {
        double ratio = duration > TimeSpan.Zero ? Math.Clamp(position / duration, 0, 1) : 0;
        _progressFill.Width = PlaybackWidth * ratio;
        _elapsed.Text = Format(position);
        _remaining.Text = Format(duration);
    }

    public void SetClock(DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        _time.Text = now.ToString("t", culture);
        _date.Text = now.ToString("ddd, d. MMM", culture);
    }

    public void SetBattery(int? percent) => _battery.Text = percent is int p ? $"{p} %" : "";

    private static string Format(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
