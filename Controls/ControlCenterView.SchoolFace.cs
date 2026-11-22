using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VNotch.Services;
using VNotch.Services.ControlCenter;

namespace VNotch.Controls;

public partial class ControlCenterView
{
    private const int SchoolStripDays = 21;
    private const double SchoolStripInset = 10;
    private const double SchoolTrackY = 37;
    private static readonly Color SchoolSage = Color.FromRgb(0x9D, 0xBD, 0xB1);
    private static readonly Color SchoolMist = Color.FromRgb(0x7F, 0xA6, 0xCC);
    private static readonly Color SchoolLilac = Color.FromRgb(0xA1, 0x9B, 0xD2);
    private static readonly Color SchoolSand = Color.FromRgb(0xD2, 0xAE, 0x7C);
    private static readonly Color SchoolRose = Color.FromRgb(0xD2, 0x85, 0x7E);
    private static readonly Color[] SchoolMarkPalette =
        [SchoolMist, SchoolLilac, SchoolSage, SchoolLilac, SchoolSand, SchoolLilac, SchoolSage, SchoolSand];
    private static readonly Brush SchoolExamGlow =
        (Brush)new RadialGradientBrush(Alpha(SchoolSand, .54), Alpha(SchoolSand, 0)).GetAsFrozen();

    internal static (Color Main, Color Warmer, Color Cooler) SchoolMood(TimeSpan? untilNext) => untilNext switch
    {
        { TotalHours: < 24 } => (SchoolRose, SchoolSand, SchoolLilac),
        { TotalHours: < 72 } => (SchoolSand, SchoolRose, SchoolSage),
        _ => (SchoolSage, SchoolLilac, SchoolMist)
    };

    private sealed record SchoolEvent(string Key, DateTimeOffset At, string Subject, string Title, string Kind,
        PiDeadline? Deadline = null, PiExam? Exam = null, PiLesson? Lesson = null)
    {
        public string Revision => $"{At.ToUnixTimeSeconds()}|{Subject}|{Title}|{Kind}|{Lesson?.Room}|{Lesson?.End}";
        public bool HasTime => Deadline != null || TimeSpan.TryParseExact(Exam?.Start, @"hh\:mm", CultureInfo.InvariantCulture, out _);
    }

    private sealed record SchoolMark(Canvas Root, Ellipse Halo, Ellipse Dot, TextBlock Label, TranslateTransform Shift)
    {
        public SchoolEvent? Event;
    }

    private readonly Dictionary<string, SchoolMark> _schoolMarks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _schoolShownMarks = new(StringComparer.Ordinal);
    private SchoolEvent[] _schoolEvents = [];
    private SchoolEvent? _schoolDefault, _schoolPreview, _schoolCover;
    private string? _schoolDetailKey;
    private DateTime _schoolStripStart;
    private DateTimeOffset? _countdownDue;
    private DispatcherTimer? _schoolCountdownTimer;
    private Grid _schoolFace = null!, _schoolHero = null!, _schoolPreviewRow = null!;
    private TextBlock _schoolUnavailable = null!, _schoolBadgeDay = null!, _schoolBadgeDate = null!,
        _schoolTitle = null!, _schoolMeta = null!, _schoolCountdown = null!, _schoolUnit = null!,
        _schoolPreviewSubject = null!, _schoolPreviewTitle = null!, _schoolPreviewWhen = null!;
    private Border _schoolBadge = null!, _schoolDetail = null!, _schoolRange = null!, _schoolNow = null!, _schoolRing = null!;
    private Canvas _schoolRibbon = null!, _schoolScale = null!, _schoolMarkLayer = null!;
    private Ellipse _schoolPreviewDot = null!;
    private readonly TranslateTransform _schoolRingShift = new(), _schoolRowShift = new();
    private readonly WeatherTween _schoolLight = new();
    private readonly Grid _schoolExamRoll = new() { Height = 16, ClipToBounds = true };
    private readonly Grid _schoolDeadlineRoll = new() { Height = 16, ClipToBounds = true };
    private TextBlock _schoolExamUnit = null!, _schoolDeadlineUnit = null!;
    private (Color Main, Color Warmer, Color Cooler)? _schoolPaint;
    private double _schoolLightStrength;
    private double _schoolRingTarget = double.NaN;
    private Point? _schoolLightCenter;
    private Point? _schoolScrubOrigin;
    private bool _schoolScrubMoved;

    private bool SchoolDataAvailable => !_isLocalOnly() && _pi?.IsConfigured == true && _piSchool != null;
    private bool AnimateSchool => IsVisible && !AnimationConfig.ReduceMotion;
    internal StackPanel SchoolStatus { get; } = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };

    private void MountSchoolFace()
    {
        var body = new StackPanel();
        _schoolFace = new Grid { Children = { body }, Visibility = Visibility.Collapsed };
        _schoolUnavailable = CompactText("", 13, CompactLabel);
        _schoolUnavailable.Margin = new Thickness(0, 9, 0, 9);
        _piSchoolSection.Children.Add(_schoolFace);
        _piSchoolSection.Children.Add(_schoolUnavailable);

        _schoolBadgeDay = CompactText("", 10, CompactLabel);
        _schoolBadgeDay.FontWeight = FontWeights.SemiBold;
        _schoolBadgeDay.TextAlignment = TextAlignment.Center;
        _schoolBadgeDate = CompactText("", 19, CompactInk);
        _schoolBadgeDate.TextAlignment = TextAlignment.Center;
        _schoolBadgeDate.Margin = new Thickness(0, 1, 0, 4);
        _schoolBadge = new Border
        {
            Width = 48, CornerRadius = new CornerRadius(10), Background = Frozen("#1D1D20"),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 14, 0),
            ClipToBounds = true, Child = new StackPanel
            {
                Children =
                {
                    new Border { Background = Frozen("#12FFFFFF"), Padding = new Thickness(0, 3, 0, 2), Child = _schoolBadgeDay },
                    _schoolBadgeDate
                }
            }
        };
        _schoolTitle = CompactText("", 22, CompactInk);
        _schoolTitle.FontWeight = FontWeights.SemiBold;
        _schoolTitle.TextWrapping = TextWrapping.Wrap;
        _schoolTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        _schoolTitle.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _schoolTitle.LineHeight = 26;
        _schoolTitle.MaxHeight = 52;
        _schoolTitle.VerticalAlignment = VerticalAlignment.Top;
        _schoolMeta = CompactText("", 12, CompactLabel);
        _schoolMeta.Margin = new Thickness(0, 5, 0, 0);
        var labels = new StackPanel { Children = { _schoolTitle, _schoolMeta } };
        _schoolCountdown = CompactText("", 32, CompactInk);
        _schoolCountdown.TextAlignment = TextAlignment.Right;
        _schoolCountdown.TextTrimming = TextTrimming.None;
        Typography.SetNumeralAlignment(_schoolCountdown, FontNumeralAlignment.Tabular);
        _schoolUnit = CompactText("", 11, CompactLabel);
        _schoolUnit.TextAlignment = TextAlignment.Right;
        var countdown = new StackPanel
        {
            Margin = new Thickness(8, 0, 0, 0), Children = { _schoolCountdown, _schoolUnit }
        };
        _schoolHero = new Grid { MinHeight = 55, Background = Brushes.Transparent };
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            _schoolHero.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        _schoolHero.Children.Add(_schoolBadge);
        Grid.SetColumn(labels, 1);
        _schoolHero.Children.Add(labels);
        Grid.SetColumn(countdown, 2);
        _schoolHero.Children.Add(countdown);
        _schoolHero.MouseLeftButtonUp += (_, e) =>
        {
            if (_schoolDefault == null) return;
            e.Handled = true;
            SelectSchoolPreview(_schoolDefault);
            ToggleSchoolDetail();
        };
        _schoolHero.SizeChanged += (_, _) => GatherSchoolLightSoon();
        body.Children.Add(_schoolHero);

        body.Children.Add(BuildSchoolRibbon());
        _schoolPreviewDot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 9, 0) };
        _schoolPreviewSubject = CompactText("", 11.5, CompactInk, 50);
        _schoolPreviewSubject.FontWeight = FontWeights.SemiBold;
        _schoolPreviewTitle = CompactText("", 12, CompactInk);
        _schoolPreviewTitle.Margin = new Thickness(9, 0, 8, 0);
        _schoolPreviewWhen = CompactText("", 11, CompactLabel, 175);
        _schoolPreviewRow = new Grid
        {
            MinHeight = 27, Background = Brushes.Transparent, Cursor = Cursors.Hand, RenderTransform = _schoolRowShift
        };
        foreach (var child in new UIElement[] { _schoolPreviewDot, _schoolPreviewSubject, _schoolPreviewTitle, _schoolPreviewWhen })
        {
            int column = _schoolPreviewRow.Children.Count;
            _schoolPreviewRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = column == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
            });
            Grid.SetColumn(child, column);
            _schoolPreviewRow.Children.Add(child);
        }
        _schoolPreviewRow.MouseLeftButtonUp += (_, e) => { e.Handled = true; ToggleSchoolDetail(); };
        body.Children.Add(_schoolPreviewRow);
        _schoolDetail = new Border { Height = 0, ClipToBounds = true, Child = new Grid() };
        body.Children.Add(_schoolDetail);

        _schoolExamUnit = CompactText("", 11.5, CompactInk);
        _schoolDeadlineUnit = CompactText("", 11.5, CompactInk);
        SchoolStatus.Children.Add(_schoolExamRoll);
        SchoolStatus.Children.Add(_schoolExamUnit);
        SchoolStatus.Children.Add(_schoolDeadlineRoll);
        SchoolStatus.Children.Add(_schoolDeadlineUnit);
        _schoolLight.Step = value => { if (_backdrop != null) _backdrop.Intensity = value; };
    }

    private FrameworkElement BuildSchoolRibbon()
    {
        _schoolScale = new Canvas();
        _schoolMarkLayer = new Canvas();
        _schoolRange = new Border { Height = 4, CornerRadius = new CornerRadius(2) };
        _schoolNow = new Border { Width = 2, Height = 21, Background = CompactInk, CornerRadius = new CornerRadius(1) };
        _schoolRing = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5),
            BorderBrush = CompactInk, RenderTransform = _schoolRingShift, Visibility = Visibility.Collapsed
        };
        _schoolRibbon = new Canvas
        {
            Height = 73, Margin = new Thickness(0, 16, 0, 0), Background = Brushes.Transparent, Cursor = Cursors.SizeWE,
            Focusable = true, Children = { _schoolScale, _schoolRange, _schoolMarkLayer, _schoolRing, _schoolNow }
        };
        foreach (UIElement child in _schoolRibbon.Children) child.IsHitTestVisible = false;
        Canvas.SetTop(_schoolRange, SchoolTrackY - 2);
        Canvas.SetTop(_schoolRing, SchoolTrackY - 9);
        Canvas.SetTop(_schoolNow, 24);
        _schoolRibbon.SizeChanged += (_, _) => LayoutSchoolStrip();
        _schoolRibbon.MouseLeftButtonDown += (_, e) =>
        {
            if (_schoolEvents.Length == 0 || !_schoolRibbon.CaptureMouse()) return;
            e.Handled = true;
            _schoolRibbon.Focus();
            _schoolScrubOrigin = e.GetPosition(_schoolRibbon);
            _schoolScrubMoved = false;
            ScrubSchoolStrip(_schoolScrubOrigin.Value.X, cycle: true);
        };
        _schoolRibbon.MouseMove += (_, e) =>
        {
            if (_schoolScrubOrigin is not { } origin) return;
            var position = e.GetPosition(_schoolRibbon);
            if (!_schoolScrubMoved && Math.Abs(position.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            _schoolScrubMoved = true;
            ScrubSchoolStrip(position.X, cycle: false);
            e.Handled = true;
        };
        _schoolRibbon.MouseLeftButtonUp += (_, e) =>
        {
            if (_schoolScrubOrigin == null) return;
            e.Handled = true;
            _schoolScrubOrigin = null;
            _schoolRibbon.ReleaseMouseCapture();
            if (_schoolScrubMoved) SelectSchoolPreview(_schoolDefault, animateRing: true);
        };
        _schoolRibbon.LostMouseCapture += (_, _) =>
        {
            if (_schoolScrubOrigin == null) return;
            _schoolScrubOrigin = null;
            SelectSchoolPreview(_schoolDefault);
        };
        _schoolRibbon.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _schoolPreview != null) ToggleSchoolDetail(animate: false);
            else if (e.Key == Key.Escape) SelectSchoolPreview(_schoolDefault);
            else if ((e.Key == Key.Left || e.Key == Key.Right) && _schoolEvents.Length > 0)
            {
                int index = Array.FindIndex(_schoolEvents, item => item.Key == _schoolPreview?.Key);
                SelectSchoolPreview(_schoolEvents[Math.Clamp(index + (e.Key == Key.Right ? 1 : -1), 0, _schoolEvents.Length - 1)]);
            }
            else return;
            e.Handled = true;
        };
        return _schoolRibbon;
    }

    private void RefreshSchoolFace()
    {
        bool available = SchoolDataAvailable;
        _schoolFace.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        SchoolStatus.Visibility = available && IsVisible ? Visibility.Visible : Visibility.Collapsed;
        _schoolUnavailable.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        if (!available)
        {
            _schoolUnavailable.Text = Loc.Get(_isLocalOnly() ? "pi.localOnly" : _pi?.IsConfigured != true
                ? "pi.notConfigured" : _piReachable ? "pi.loading" : "pi.unreachable");
            StopSchoolFaceMotion();
            _schoolLight.Stop(0);
            if (_backdrop != null) _backdrop.Intensity = 0;
            _schoolLightStrength = 0;
            return;
        }

        var now = DateTimeOffset.Now;
        var school = _piSchool!;
        var open = school.Deadlines.Where(d => !d.Submitted).OrderBy(d => d.Due).Select(SchoolDeadlineEvent).ToArray();
        var exams = school.Exams.Where(e => e.Date.Date >= now.Date).Select(e => SchoolExamEvent(e, school)).ToArray();
        var cover = open.FirstOrDefault();
        var previousDefault = _schoolDefault;
        _schoolDefault = cover ?? exams.Where(e => e.At >= now && InSchoolStrip(e, now.Date)).MinBy(e => e.At)
            ?? exams.Where(e => InSchoolStrip(e, now.Date)).MinBy(e => e.At);
        _schoolCover = cover;
        _countdownDue = cover?.At;
        var events = open.Concat(exams).Where(e => InSchoolStrip(e, now.Date)).OrderBy(e => e.At)
            .DistinctBy(e => e.Key).ToArray();
        bool newDay = _schoolStripStart != now.Date;
        _schoolStripStart = now.Date;
        _schoolEvents = events;

        bool coverChanged = Seen.Changed("school:cover", cover == null ? "" : cover.Key + "|" + cover.Revision);
        bool animateCover = coverChanged && AnimateSchool;
        ShowSchoolCover(now);
        RefreshSchoolMarks(events, cover, now);
        if (newDay) LayoutSchoolStrip();
        ShowSchoolTime(now, animateCover);
        if (_schoolLightCenter == null) GatherSchoolLightSoon();
        var selected = _schoolPreview?.Key != previousDefault?.Key
            ? events.FirstOrDefault(e => e.Key == _schoolPreview?.Key) : null;
        var preview = selected ?? _schoolDefault;
        string previewValue = _schoolDefault == null ? "" : _schoolDefault.Key + "|" + _schoolDefault.Revision;
        bool previewChanged = Seen.Changed("school:preview", previewValue);
        SelectSchoolPreview(preview, animateRow: selected == null && previewChanged && AnimateSchool);
        RefreshSchoolCounts(exams.Length, open.Length);
        AutomationProperties.SetName(_schoolRibbon, Say("Nächste drei Wochen", "Next three weeks"));
        UpdateCountdownVisibility();
    }

    private static bool InSchoolStrip(SchoolEvent item, DateTime start) =>
        (item.At.LocalDateTime.Date - start).TotalDays is >= 0 and < SchoolStripDays;

    private static SchoolEvent SchoolDeadlineEvent(PiDeadline deadline)
    {
        string kind = deadline.Kind switch
        {
            "classroom" => "Classroom 50",
            "nw" => "NW-Abgaben",
            _ => "Moodle"
        };
        var due = deadline.Due.LocalDateTime;
        string title = deadline.Name.Trim();
        foreach (string format in new[] { "d.M.", "d.M", "dd.MM.", "dd.MM", "d.M.yyyy", "dd.MM.yyyy" })
        {
            string suffix = " bis " + due.ToString(format, CultureInfo.InvariantCulture);
            if (!title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            title = title[..^suffix.Length].TrimEnd();
            break;
        }
        return new SchoolEvent(AssignmentKey(deadline), deadline.Due, DeadlineSubject(deadline), title, kind, deadline);
    }

    private static SchoolEvent SchoolExamEvent(PiExam exam, PiSchool school)
    {
        string date = exam.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string subject = ExamSubject(exam);
        var lessons = school.Days.TryGetValue(date, out var day) ? day : date == school.Date ? school.Lessons : [];
        var matching = lessons.Where(l => !l.Cancelled && exam.Start.Length > 0 && l.Start == exam.Start &&
            (l.SubjectKey == exam.Subject && exam.Subject.Length > 0 || SchoolSubject(l.Subject) == subject)).Take(2).ToArray();
        string key = $"exam:{date}|{exam.Start}|{exam.Subject}|{exam.Kind}|{exam.Title}";
        return new SchoolEvent(key, SchoolExamTime(exam), subject, exam.Title.Length > 0 ? exam.Title : exam.Kind,
            exam.Kind, Exam: exam, Lesson: matching.Length == 1 ? matching[0] : null);
    }

    private void ShowSchoolCover(DateTimeOffset now)
    {
        var cover = _schoolCover;
        _schoolHero.Cursor = _schoolDefault == null ? Cursors.Arrow : Cursors.Hand;
        _schoolBadge.Visibility = _schoolCountdown.Visibility = cover == null ? Visibility.Collapsed : Visibility.Visible;
        if (cover == null)
        {
            _schoolTitle.Text = Loc.Get("school.nothingDue");
            _schoolTitle.ToolTip = null;
            _schoolMeta.Text = _schoolUnit.Text = "";
            return;
        }
        var local = cover.At.LocalDateTime;
        _schoolBadgeDay.Text = local.ToString("ddd", CultureInfo.CurrentUICulture).ToUpper(CultureInfo.CurrentUICulture);
        _schoolBadgeDate.Text = local.Day.ToString(CultureInfo.CurrentCulture);
        _schoolTitle.Text = cover.Title;
        _schoolTitle.ToolTip = cover.Title;
        ShowSchoolDueTime(now, cover);
    }

    private void ShowSchoolDueTime(DateTimeOffset now, SchoolEvent cover)
    {
        var date = cover.At.LocalDateTime;
        string at = date.ToString("HH:mm", CultureInfo.CurrentUICulture);
        string time = cover.At <= now ? Say($"fällig um {at}", $"due at {at}")
            : date.Date == now.Date ? Say($"heute bis {at}", $"today by {at}") : Say($"bis {at}", $"by {at}");
        _schoolMeta.Text = $"{cover.Subject} · {cover.Kind} · {time}";
        _schoolMeta.ToolTip = cover.Deadline?.Course;
    }

    private void RefreshSchoolCounts(int exams, int deadlines)
    {
        bool examChanged = Seen.Changed("school:exams", exams);
        bool deadlineChanged = Seen.Changed("school:deadlines", deadlines);
        if (!IsVisible) return;
        _schoolExamUnit.Text = Say(" Tests · ", exams == 1 ? " test · " : " tests · ");
        _schoolDeadlineUnit.Text = deadlines == 1 ? Say(" Abgabe", " deadline") : Say(" Abgaben", " deadlines");
        Roll(_schoolExamRoll, exams, examChanged);
        Roll(_schoolDeadlineRoll, deadlines, deadlineChanged);

        void Roll(Grid holder, int value, bool changed)
        {
            int direction = holder.Tag is string old && int.TryParse(old, out int previous) && value < previous ? -1 : 1;
            SwapCompactLabel(holder, value.ToString(CultureInfo.CurrentCulture),
                horizontal: false, direction: direction, size: 11.5, animate: changed && AnimateSchool);
        }
    }

    private void RefreshSchoolMarks(SchoolEvent[] events, SchoolEvent? cover, DateTimeOffset now)
    {
        var current = events.ToDictionary(e => e.Key, StringComparer.Ordinal);
        bool rosterChanged = Seen.Changed("school:marks", string.Join("\n", current.Keys.Order(StringComparer.Ordinal)));
        foreach (string key in _schoolMarks.Keys.Where(key => !current.ContainsKey(key)).ToArray())
        {
            var mark = _schoolMarks[key];
            Motion.Stop(mark.Shift, TranslateTransform.XProperty, 0);
            Motion.Stop(mark.Shift, TranslateTransform.YProperty, 0);
            _schoolMarkLayer.Children.Remove(mark.Root);
            _schoolMarks.Remove(key);
        }
        if (IsVisible)
        {
            foreach (string key in _schoolShownMarks.Keys.Where(key => !current.ContainsKey(key)).ToArray())
            {
                Seen.Forget("school:mark:" + key);
                _schoolShownMarks.Remove(key);
            }
        }
        foreach (var item in events)
        {
            if (!_schoolMarks.TryGetValue(item.Key, out var mark))
            {
                var halo = new Ellipse { Width = 40, Height = 40, Fill = SchoolExamGlow };
                var dot = new Ellipse { Stroke = Brushes.Black, StrokeThickness = 2 };
                var label = CompactText("", 10.5, CompactLabel, 50);
                label.FontWeight = FontWeights.SemiBold;
                var shift = new TranslateTransform();
                var root = new Canvas { RenderTransform = shift, Children = { halo, dot, label } };
                mark = new SchoolMark(root, halo, dot, label, shift);
                Canvas.SetLeft(halo, -20);
                Canvas.SetTop(halo, SchoolTrackY - 20);
                _schoolMarks.Add(item.Key, mark);
                _schoolMarkLayer.Children.Add(root);
            }
            bool revised = Seen.Changed("school:mark:" + item.Key, item.Revision);
            bool wasShown = _schoolShownMarks.TryGetValue(item.Key, out var before);
            if (AnimateSchool && (revised || rosterChanged && !wasShown))
            {
                double offset = wasShown ? SchoolX(before) - SchoolX(item.At) : 0;
                if (Math.Abs(offset) > .1) Motion.To(mark.Shift, TranslateTransform.XProperty, 0, Motion.Shift, from: offset);
                else Motion.To(mark.Shift, TranslateTransform.YProperty, 0, Motion.Shift, from: -6);
            }
            if (IsVisible) _schoolShownMarks[item.Key] = item.At;
            mark.Event = item;
            bool flag = item.Key == cover?.Key;
            double size = flag ? 16 : 12;
            mark.Dot.Width = mark.Dot.Height = size;
            mark.Label.Text = item.Subject;
            mark.Label.Foreground = flag ? CompactInk : CompactLabel;
            Canvas.SetLeft(mark.Dot, -size / 2);
            Canvas.SetTop(mark.Dot, SchoolTrackY - size / 2);
            Panel.SetZIndex(mark.Root, flag ? 1 : 0);
        }
        PositionSchoolMarks(now);
    }

    private double SchoolX(double day) => SchoolStripInset + Math.Clamp(day, 0, SchoolStripDays) *
        Math.Max(0, _schoolRibbon.ActualWidth - 2 * SchoolStripInset) / SchoolStripDays;

    private double SchoolX(DateTimeOffset at) => SchoolX((at.LocalDateTime - _schoolStripStart).TotalDays);

    private void LayoutSchoolStrip()
    {
        if (_schoolStripStart == default || _schoolRibbon.ActualWidth <= 0) return;
        _schoolScale.Children.Clear();
        for (int day = 0; day < SchoolStripDays; day++)
        {
            var date = _schoolStripStart.AddDays(day);
            double x = SchoolX(day);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                int days = date.DayOfWeek == DayOfWeek.Saturday ? Math.Min(2, SchoolStripDays - day) : day == 0 ? 1 : 0;
                if (days > 0) Add(new Border { Width = SchoolX(day + days) - x, Height = 16, CornerRadius = new CornerRadius(4),
                    Background = Frozen("#0DFFFFFF") }, x, SchoolTrackY - 8);
            }
            if (date.DayOfWeek == DayOfWeek.Monday)
                Add(CompactText(date.ToString("d.M.", CultureInfo.CurrentUICulture), 10, CompactLabel), x, 15);
        }
        Add(new Border { Width = SchoolX(SchoolStripDays) - SchoolX(0), Height = 2, Background = Frozen("#24FFFFFF"),
            CornerRadius = new CornerRadius(1) }, SchoolX(0), SchoolTrackY - 1);
        for (int day = 0; day <= SchoolStripDays; day++)
            Add(new Rectangle { Width = 1, Height = 6, Fill = Frozen("#34FFFFFF") }, SchoolX(day), SchoolTrackY - 3);
        PositionSchoolMarks(DateTimeOffset.Now);
        PositionSchoolRing(animate: false);
        ShowSchoolRange(DateTimeOffset.Now, animate: false);

        void Add(FrameworkElement element, double x, double y)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            _schoolScale.Children.Add(element);
        }
    }

    private void PositionSchoolMarks(DateTimeOffset now)
    {
        double[] laneEnds = [0, 0];
        var mood = SchoolMood(_countdownDue - now);
        foreach (var item in _schoolEvents)
        {
            if (!_schoolMarks.TryGetValue(item.Key, out var mark)) continue;
            bool flag = item.Key == _schoolCover?.Key;
            double x = SchoolX(item.At);
            Canvas.SetLeft(mark.Root, x);
            var color = flag ? mood.Main : SchoolMarkPalette[SubjectColorIndex(item.Subject)];
            if (mark.Dot.Fill is not SolidColorBrush fill || fill.Color != color) mark.Dot.Fill = new SolidColorBrush(color);
            mark.Halo.Visibility = item.Exam != null && item.At - now is { TotalHours: >= 0 and <= 48 }
                ? Visibility.Visible : Visibility.Collapsed;
            mark.Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double width = mark.Label.DesiredSize.Width;
            double left = Math.Clamp(x - width / 2, 0, Math.Max(0, _schoolRibbon.ActualWidth - width));
            int lane = left >= laneEnds[0] ? 0 : left >= laneEnds[1] ? 1 : -1;
            mark.Label.Visibility = flag || lane >= 0 ? Visibility.Visible : Visibility.Hidden;
            if (!flag && lane >= 0) laneEnds[lane] = left + width + 4;
            Canvas.SetLeft(mark.Label, left - x);
            Canvas.SetTop(mark.Label, flag ? 0 : 46 + 13 * Math.Max(0, lane));
        }
    }

    private void ShowSchoolRange(DateTimeOffset now, bool animate)
    {
        if (_schoolRibbon.ActualWidth <= 0) return;
        double start = SchoolX(now);
        double width = _countdownDue is { } due ? Math.Max(0, SchoolX(due) - start) : 0;
        Canvas.SetLeft(_schoolNow, start - 1);
        Canvas.SetLeft(_schoolRange, start);
        if (animate) Motion.To(_schoolRange, WidthProperty, width, Motion.Shift);
        else if (!Motion.IsRunning(_schoolRange, WidthProperty)) Motion.Stop(_schoolRange, WidthProperty, width);
    }

    private void ScrubSchoolStrip(double x, bool cycle)
    {
        var closest = _schoolEvents.MinBy(e => Math.Abs(SchoolX(e.At) - x));
        if (closest == null) return;
        var nearby = _schoolEvents.Where(e => Math.Abs(SchoolX(e.At) - SchoolX(closest.At)) < 8).ToArray();
        int current = Array.FindIndex(nearby, e => e.Key == _schoolPreview?.Key);
        if (current >= 0) closest = cycle ? nearby[(current + 1) % nearby.Length] : nearby[current];
        SelectSchoolPreview(closest, animateRing: true);
    }

    private void SelectSchoolPreview(SchoolEvent? item, bool animateRing = false, bool animateRow = false)
    {
        bool different = item?.Key != _schoolPreview?.Key;
        _schoolPreview = item;
        if (different && _schoolDetailKey != null) CloseSchoolDetail();
        _schoolPreviewRow.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
        if (item != null)
        {
            _schoolPreviewDot.Fill = new SolidColorBrush(item.Key == _schoolCover?.Key
                ? SchoolMood(_countdownDue - DateTimeOffset.Now).Main : SchoolMarkPalette[SubjectColorIndex(item.Subject)]);
            _schoolPreviewSubject.Text = item.Subject;
            _schoolPreviewTitle.Text = item.Title;
            _schoolPreviewTitle.ToolTip = item.Title;
            _schoolPreviewWhen.Text = SchoolEventDate(item, includeTime: item.Key != _schoolCover?.Key);
            _schoolPreviewWhen.ToolTip = _schoolPreviewWhen.Text;
        }
        PositionSchoolRing(animateRing && different);
        if (animateRow) Motion.To(_schoolRowShift, TranslateTransform.YProperty, 0, Motion.Shift, from: -5);
    }

    private static string SchoolEventDate(SchoolEvent item, bool includeTime)
    {
        var local = item.At.LocalDateTime;
        string when = local.ToString(includeTime && item.HasTime ? "ddd d.M., HH:mm" : "ddd d.M.", CultureInfo.CurrentUICulture);
        return item.Lesson?.Room is { Length: > 0 } room ? $"{when} · {room}" : when;
    }

    private void PositionSchoolRing(bool animate)
    {
        var item = _schoolPreview;
        bool show = item != null && _schoolMarks.ContainsKey(item.Key);
        bool alreadyShown = _schoolRing.Visibility == Visibility.Visible;
        _schoolRing.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show || item == null)
        {
            if (alreadyShown) Motion.Stop(_schoolRingShift, TranslateTransform.XProperty);
            return;
        }
        double x = SchoolX(item.At) - _schoolRing.Width / 2;
        if (x == _schoolRingTarget && alreadyShown) return;
        _schoolRingTarget = x;
        if (animate && alreadyShown && AnimateSchool) Motion.To(_schoolRingShift, TranslateTransform.XProperty, x, Motion.Shift);
        else Motion.Stop(_schoolRingShift, TranslateTransform.XProperty, x);
    }

    private void ToggleSchoolDetail(bool animate = true)
    {
        if (_schoolPreview is not { } item) return;
        if (_schoolDetailKey == item.Key)
        {
            _schoolDetailKey = null;
            FoldInline(_schoolDetail, false, animate);
            return;
        }
        _schoolDetailKey = item.Key;
        RefreshSchoolDetail(animate);
    }

    private void RefreshSchoolDetail(bool animate = false)
    {
        if (_schoolDetailKey == null || _schoolPreview is not { } item) return;
        if (item.Deadline is { } deadline) _schoolDetail.Child = AssignmentPanel(deadline);
        else
        {
            string time = !item.HasTime ? "" : item.Lesson?.End is { Length: > 0 } end
                ? $"{item.Exam!.Start}–{end}" : item.Exam!.Start;
            var lines = new[] { item.Title, item.Lesson?.Subject, time, item.Lesson?.Room }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal);
            var detail = CompactText(string.Join(" · ", lines), 11.5, CompactLabel);
            detail.TextWrapping = TextWrapping.Wrap;
            var link = CompactPill(Say("In Untis öffnen", "Open in Untis"), async () =>
            {
                if (SchoolLinks.Untis(_untisUrl()) is { } url) Launch(await SignInToSchoolAsync(url));
            });
            link.Margin = new Thickness(0, 5, 0, 0);
            _schoolDetail.Child = new StackPanel { Margin = new Thickness(18, 3, 0, 8), Children = { detail, link } };
        }
        FoldInline(_schoolDetail, true, animate);
    }

    private void CloseSchoolDetail()
    {
        if (_schoolDetailKey == null) return;
        _schoolDetailKey = null;
        FoldInline(_schoolDetail, false, animate: false);
    }

    internal static (string Value, string Unit, double Size) SchoolCountdown(DateTimeOffset due, DateTimeOffset now)
    {
        var left = due - now;
        if (left <= TimeSpan.Zero) return (Say("Überfällig", "Overdue"), "", 19);
        if (left.TotalHours >= 72) return ($"{(int)left.TotalDays}", Say("Tage", "days"), 32);
        if (left.TotalHours >= 24) return ($"{(int)left.TotalHours}", Say("Stunden", "hours"), 32);
        return (left.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture), "", 25);
    }

    private void ShowSchoolTime(DateTimeOffset now, bool animateCover = false)
    {
        if (_countdownDue is { } due)
        {
            var (value, unit, size) = SchoolCountdown(due, now);
            var unitVisibility = unit.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            bool refit = _schoolCountdown.FontSize != size || _schoolUnit.Visibility != unitVisibility;
            // The mounted text block keeps the same position and geometry as the clock ticks.
            if (_schoolCountdown.Text != value) _schoolCountdown.Text = value;
            _schoolCountdown.FontSize = size;
            _schoolUnit.Text = unit;
            _schoolUnit.Visibility = unitVisibility;
            if (_schoolCover is { } cover) ShowSchoolDueTime(now, cover);
            if (due <= now) _schoolCountdownTimer?.Stop();
            if (refit && IsVisible) ContentHeightChanged?.Invoke();
        }
        var left = _countdownDue - now;
        var mood = SchoolMood(left);
        if (mood != _schoolPaint)
        {
            _schoolPaint = mood;
            _schoolRange.Background = new LinearGradientBrush(Alpha(mood.Main, .22), mood.Main, 0);
            _backdrop?.SetMood(mood.Main, mood.Warmer, mood.Cooler, 0);
            PositionSchoolMarks(now);
            if (_schoolPreview?.Key == _schoolCover?.Key) _schoolPreviewDot.Fill = new SolidColorBrush(mood.Main);
        }
        double strength = left switch { null => 0, { TotalHours: < 24 } => .92, { TotalHours: < 72 } => .75, _ => .59 };
        if (animateCover) Motion.To(_schoolLight, WeatherTween.ValueProperty, strength, Motion.Shift, from: Math.Min(1, strength + .24));
        else if (strength != _schoolLightStrength) _schoolLight.Stop(strength);
        _schoolLightStrength = strength;
        ShowSchoolRange(now, animateCover);
    }

    private void GatherSchoolLightSoon() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
    {
        if (_disposed || _backdrop == null || !_schoolHero.IsVisible || !_backdrop.IsVisible || _schoolHero.ActualWidth <= 0) return;
        var center = _schoolHero.TranslatePoint(new Point(_schoolHero.ActualWidth * .38, _schoolHero.ActualHeight * .38), _backdrop);
        if (_schoolLightCenter == center) return;
        _schoolLightCenter = center;
        if (_schoolPaint is { } mood) _backdrop.SetMood(mood.Main, mood.Warmer, mood.Cooler, 0);
        _backdrop.Gather(center, verticalScale: .56);
        _backdrop.Intensity = _schoolLight.Value;
    }));

    private void TickSchoolCountdown()
    {
        var now = DateTimeOffset.Now;
        if (now.Date != _schoolStripStart) { BuildPiPage(); return; }
        ShowSchoolTime(now);
        foreach (var mark in _schoolMarks.Values)
            mark.Halo.Visibility = mark.Event is { Exam: not null } item && item.At - now is { TotalHours: >= 0 and <= 48 }
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCountdownVisibility()
    {
        if (_disposed || !IsVisible || !SchoolDataAvailable) { _schoolCountdownTimer?.Stop(); return; }
        if (_schoolCountdownTimer == null)
        {
            _schoolCountdownTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
            _schoolCountdownTimer.Tick += OnSchoolCountdownTick;
        }
        if (_countdownDue > DateTimeOffset.Now || _schoolEvents.Any(e => e.Exam != null && e.At > DateTimeOffset.Now))
            _schoolCountdownTimer.Start();
        else _schoolCountdownTimer.Stop();
    }

    private void OnSchoolCountdownTick(object? sender, EventArgs e)
    {
        if (!IsVisible || _disposed) { _schoolCountdownTimer?.Stop(); return; }
        TickSchoolCountdown();
        UpdateCountdownVisibility();
    }

    private void StopSchoolFaceMotion()
    {
        _schoolCountdownTimer?.Stop();
        SchoolStatus.Visibility = Visibility.Collapsed;
        if (_schoolRibbon == null) return;
        _schoolScrubOrigin = null;
        if (_schoolRibbon.IsMouseCaptured) _schoolRibbon.ReleaseMouseCapture();
        SelectSchoolPreview(_schoolDefault);
        CloseSchoolDetail();
        Motion.Stop(_schoolRange, WidthProperty, double.IsNaN(_schoolRange.Width) ? 0 : _schoolRange.Width);
        Motion.Stop(_schoolRingShift, TranslateTransform.XProperty,
            double.IsNaN(_schoolRingTarget) ? 0 : _schoolRingTarget);
        Motion.Stop(_schoolRowShift, TranslateTransform.YProperty, 0);
        foreach (var mark in _schoolMarks.Values)
        {
            Motion.Stop(mark.Shift, TranslateTransform.XProperty, 0);
            Motion.Stop(mark.Shift, TranslateTransform.YProperty, 0);
        }
        _schoolLight.Stop(_schoolLightStrength);
    }
}
