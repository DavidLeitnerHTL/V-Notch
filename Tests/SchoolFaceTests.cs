using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VNotch.Controls;
using VNotch.Services;
using VNotch.Services.ControlCenter;
using Xunit;
using static VNotch.Tests.HomeSchoolTabTests;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class SchoolFaceTests
{
    internal static PiSchool SampleSchool(DateTimeOffset now, DateTimeOffset? due = null)
    {
        var today = now.Date;
        var deadline = due ?? new DateTimeOffset(today.AddDays(19));
        var exam = new PiExam(today.AddDays(1), "13:40", "Test", "Test 1", "0SYP");
        var lesson = new PiLesson("13:40", "14:30", "Systemplanung und Projektentwicklung", "E58-2", false, "", 820, 870, "0SYP");
        return new PiSchool(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), true, [],
            [new PiDeadline("Abgabe Referatsunterlagen", "REL 3AHIF Rockenschaub2627", deadline, false)],
            [
                exam,
                new PiExam(today.AddDays(2), "10:00", "Test", "SQL-Abfragen", "0DBI"),
                new PiExam(today.AddDays(6), "10:55", "Veranstaltung", "Externe Veranstaltung", "EXT")
            ], null)
        {
            Days = new Dictionary<string, IReadOnlyList<PiLesson>>
            {
                [exam.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = [lesson]
            }
        };
    }

    [Fact]
    public void FaceFitsTheLongTitleAndRevealsTheSelectedTestsRoom() => SharedStaTestRunner.Run(() =>
    {
        using var client = new PiDashboardClient(() => "http://127.0.0.1:1");
        using var view = new ControlCenterView { Page = ControlCenterPage.School };
        Attach(view, client);
        var school = SampleSchool(DateTimeOffset.Now);
        string title = "Referatsunterlagen und die vollständige schriftliche Ausarbeitung";
        var coincident = school.Exams[0] with { Title = "Nachprüfung" };
        SetField(view, "_piSchool", school with
        {
            Deadlines = [school.Deadlines[0] with { Name = title }],
            Exams = school.Exams.Append(coincident).ToArray()
        });
        Invoke(view, "BuildPiPage");
        view.Measure(new Size(488, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, 488, view.DesiredSize.Height));
        view.UpdateLayout();

        var heading = (TextBlock)Field(view, "_schoolTitle");
        var countdown = (TextBlock)Field(view, "_schoolCountdown");
        Assert.Equal(title, heading.Text);
        Assert.Equal(22, heading.FontSize);
        Assert.InRange(heading.ActualHeight, 51, 52);
        Assert.Equal(Color.FromRgb(245, 245, 247), ((SolidColorBrush)countdown.Foreground).Color);
        var layer = (Canvas)Field(view, "_schoolMarkLayer");
        Assert.Equal(5, layer.Children.Count);
        var positions = layer.Children.Cast<UIElement>().Select(Canvas.GetLeft).ToArray();
        Assert.Equal(positions.Order(), positions);
        Assert.True(layer.IsDescendantOf(view));
        Assert.True(view.DesiredSize.Height < 225);

        Invoke(view, "ScrubSchoolStrip", positions[0], false);
        Assert.Contains("E58-2", ((TextBlock)Field(view, "_schoolPreviewWhen")).Text);
        Invoke(view, "ToggleSchoolDetail", false);
        var detail = (Border)Field(view, "_schoolDetail");
        Assert.True(detail.Height > 0);
        Assert.Contains(Descendants<TextBlock>(detail), t => t.Text.Contains("13:40–14:30", StringComparison.Ordinal));
        Invoke(view, "ScrubSchoolStrip", positions[0], true);
        Assert.Equal("Nachprüfung", ((TextBlock)Field(view, "_schoolPreviewTitle")).Text);
        Invoke(view, "ScrubSchoolStrip", positions[3], false);
        Assert.DoesNotContain("E58-2", ((TextBlock)Field(view, "_schoolPreviewWhen")).Text);
        Assert.Equal(0, detail.Height);
    });

    [Fact]
    public void CountdownUsesDaysHoursClockAndOverdueThresholds() => SharedStaTestRunner.Run(() =>
    {
        string previous = Loc.CurrentLanguage;
        Loc.SetLanguage("de");
        try
        {
            var now = DateTimeOffset.Parse("2026-11-16T12:00:00+01:00", CultureInfo.InvariantCulture);
            Assert.Equal(("3", "Tage", 32d), ControlCenterView.SchoolCountdown(now.AddHours(72), now));
            Assert.Equal(("32", "Stunden", 32d), ControlCenterView.SchoolCountdown(now.AddHours(32), now));
            Assert.Equal(("05:04:03", "", 25d), ControlCenterView.SchoolCountdown(now.AddHours(5).AddMinutes(4).AddSeconds(3), now));
            Assert.Equal(("Überfällig", "", 19d), ControlCenterView.SchoolCountdown(now, now));
        }
        finally { Loc.SetLanguage(previous); }
    });

    [Fact]
    public void AChangedMarkSlidesOnceAndStopsOnHide() => SharedStaTestRunner.RunAsync(async ct =>
    {
        BackgroundTestWindows.EnsureApplicationResources();
        bool reduce = AnimationConfig.ReduceMotion;
        bool? clientAnimations = AnimationConfig.ClientAreaAnimationOverride;
        AnimationConfig.ClientAreaAnimationOverride = true;
        AnimationConfig.SetReduceMotion(false);
        using var client = new PiDashboardClient(() => "http://127.0.0.1:1");
        using var view = new ControlCenterView { Page = ControlCenterPage.School };
        var host = new Grid { Children = { view, view.SchoolStatus } };
        var window = new Window { Content = host, Width = 520, Height = 450, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            Attach(view, client);
            var school = SampleSchool(DateTimeOffset.Now);
            var seenAt = DateTime.UtcNow;
            SetField(view, "_seen", new TabLedger(() => view.IsVisible, () => seenAt));
            SetField(view, "_piSchool", school);
            Invoke(view, "BuildPiPage");
            window.Show();
            view.UpdateLayout();
            var newExam = new PiExam(DateTime.Today.AddDays(5), "08:00", "Test", "Neue Prüfung", "NSCS");
            string key = $"exam:{newExam.Date:yyyy-MM-dd}|08:00|NSCS|Test|Neue Prüfung";
            var marks = (IDictionary)Field(view, "_schoolMarks");
            Assert.All(marks.Values.Cast<object>(), mark => Assert.False(Shift(mark).HasAnimatedProperties));

            view.Visibility = Visibility.Collapsed;
            SetField(view, "_piSchool", school with { Exams = school.Exams.Append(newExam).ToArray() });
            Invoke(view, "BuildPiPage");
            var shift = Shift(marks[key]!);
            Assert.False(shift.HasAnimatedProperties);
            Assert.False(((System.Windows.Threading.DispatcherTimer)Field(view, "_schoolCountdownTimer")).IsEnabled);

            seenAt += TabLedger.Away;
            view.Visibility = Visibility.Visible;
            Assert.True(Motion.IsRunning(shift, TranslateTransform.YProperty));
            var exams = (Grid)Field(view, "_schoolExamRoll");
            var rolled = Assert.IsType<TextBlock>(exams.Children[^1]);
            var roll = Assert.IsType<TranslateTransform>(rolled.RenderTransform);
            Assert.True(Motion.IsRunning(roll, TranslateTransform.YProperty));
            await WpfFrameWaiter.UntilAsync(() => !Motion.IsRunning(shift, TranslateTransform.YProperty), "school mark settles", ct);
            Invoke(view, "BuildPiPage");
            Assert.False(Motion.IsRunning(shift, TranslateTransform.YProperty));

            SetField(view, "_piSchool", school with
            {
                Deadlines = [school.Deadlines[0] with { Due = school.Deadlines[0].Due.AddHours(-2) }],
                Exams = school.Exams.Append(newExam).ToArray()
            });
            var light = (WeatherTween)Field(view, "_schoolLight");
            Invoke(view, "BuildPiPage");
            Assert.True(Motion.IsRunning(light, WeatherTween.ValueProperty));
            view.Visibility = Visibility.Collapsed;
            Assert.False(shift.HasAnimatedProperties);
            Assert.False(light.HasAnimatedProperties);
            Assert.False(roll.HasAnimatedProperties);
        }
        finally
        {
            window.Close();
            AnimationConfig.ClientAreaAnimationOverride = clientAnimations;
            AnimationConfig.SetReduceMotion(reduce);
        }
    });

    private static void Attach(ControlCenterView view, PiDashboardClient client)
    {
        SetField(view, "_piRefreshing", true);
        view.AttachPi(client, () => false, () => false, () => "", () => "");
    }

    private static TranslateTransform Shift(object mark) => ((Canvas)mark.GetType().GetProperty("Root")!.GetValue(mark)!).RenderTransform as TranslateTransform
        ?? throw new InvalidOperationException("A school mark must have a translation.");
}
