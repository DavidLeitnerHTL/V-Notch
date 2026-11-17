using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VNotch.Controls;
using VNotch.Controllers;
using VNotch.Models;
using VNotch.Services;
using VNotch.Services.ControlCenter;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class ConsistencyFilmTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly string Output = Path.Combine(Path.GetTempPath(), "vnotch-film-v2-6", Environment.GetEnvironmentVariable("VNOTCH_FILM_ROUND") ?? "round1");
    private static object? Call(object o, string name, params object?[] args) => o.GetType().GetMethods(Private).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(o, args);
    private static T Get<T>(object o, string name) => (T)o.GetType().GetField(name, Private)!.GetValue(o)!;
    private static void Set(object o, string name, object? value) => o.GetType().GetField(name, Private)!.SetValue(o, value);

    private static async Task Film(Window host, string name, Action<int> action, CancellationToken ct, int count = 36)
    {
        Directory.CreateDirectory(Output);
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var frames = new List<(BitmapSource Image, long Time)>();
        var clock = Stopwatch.StartNew();
        EventHandler render = (_, _) => { };
        CompositionTarget.Rendering += render;
        try
        {
        for (int i = 0; i < count; i++)
        {
            long frameStart = clock.ElapsedMilliseconds;
            action(i);
            host.UpdateLayout();
            var root = (FrameworkElement)host.Content;
            var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(root.ActualWidth * 2)), Math.Max(1, (int)Math.Ceiling(root.ActualHeight * 2)), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(root);
            bitmap.Freeze();
            frames.Add((bitmap, clock.ElapsedMilliseconds));
            await Task.Delay((int)Math.Max(1, 40 - (clock.ElapsedMilliseconds - frameStart)), ct);
        }
        }
        finally { CompositionTarget.Rendering -= render; }
        int cellW = 360, cellH = 218;
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(26, 27, 31)), null, new Rect(0, 0, cellW * 6, cellH * (int)Math.Ceiling(count / 6d)));
            for (int i = 0; i < count; i++)
            {
                int x = i % 6 * cellW, y = i / 6 * cellH;
                var frame = frames[i];
                double scale = Math.Min(cellW / frame.Image.Width, 196 / frame.Image.Height);
                dc.DrawImage(frame.Image, new Rect(x + (cellW - frame.Image.Width * scale) / 2, y + 18, frame.Image.Width * scale, frame.Image.Height * scale));
                dc.DrawText(new FormattedText($"{name} | {frame.Time} ms", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.White, 1), new Point(x + 5, y + 2));
            }
        }
        var sheet = new RenderTargetBitmap(cellW * 6, cellH * (int)Math.Ceiling(count / 6d), 96, 96, PixelFormats.Pbgra32);
        sheet.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(sheet));
        using var file = File.Create(Path.Combine(Output, name + ".png")); encoder.Save(file);
        var rest = new PngBitmapEncoder(); rest.Frames.Add(BitmapFrame.Create(frames[Math.Min(12, frames.Count - 1)].Image));
        using var resting = File.Create(Path.Combine(Output, name + "-rest.png")); rest.Save(resting);
    }

    [Fact]
    public void ShellTabsAndMusic() => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool before = AnimationConfig.ReduceMotion;
        bool? animations = AnimationConfig.ClientAreaAnimationOverride;
        AnimationConfig.ClientAreaAnimationOverride = true;
        await using var fixture = MusicMotionTests.Create();
        var w = fixture.Window;
        // Hosting only the content avoids starting the real window's services and camera.
        using var host = new Host(w, 1000, 500);
        w.SetDebugViewLock(false);
        MusicMotionTests.Apply(w, MusicMotionTests.Track("First track", Colors.IndianRed));
        try
        {
            AnimationConfig.SetReduceMotion(true);
            w.SetDebugViewState("CompactMusicPill");
            AnimationConfig.SetReduceMotion(false);
            await Film(host.Window, "shell", i =>
            {
                if (i == 0 || i == 25) w.NavigateToNotchView(NotchView.Media);
                if (i == 13 || i == 27) w.NavigateToNotchView(NotchView.Compact);
            }, ct, 44);
            foreach (bool push in new[] { false, true })
            {
                AnimationConfig.SetReduceMotion(true);
                w.NavigateToNotchView(push ? NotchView.Media : NotchView.Compact);
                await Task.Delay(80, ct);
                AnimationConfig.SetReduceMotion(false);
                DateTime start = DateTime.UtcNow;
                await Film(host.Window, push ? "push-close" : "pull-open", i =>
                {
                    if (i == 0) w.BeginShellGestureTracking(new Point(), start);
                    if (i is >= 1 and <= 7) w.UpdateGestureTracking(new Point(0, (push ? -1 : 1) * i * 13), start.AddMilliseconds(i * 40));
                    if (i == 8) w.FinishShellDrag(false, start.AddMilliseconds(280));
                    if (i == 11) w.NavigateToNotchView(push ? NotchView.Media : NotchView.Compact);
                }, ct);
            }
            AnimationConfig.SetReduceMotion(true); w.NavigateToNotchView(NotchView.Media); await Task.Delay(80, ct); AnimationConfig.SetReduceMotion(false);
            foreach (var tab in new[] { NotchView.Secondary, NotchView.Timer, NotchView.Claude, NotchView.Pi, NotchView.School, NotchView.AudioMixer, NotchView.Camera, NotchView.Media })
                await Film(host.Window, "tab-" + tab, i =>
                {
                    if (i == 0 || i == 21) w.NavigateToNotchView(tab);
                    if (i == 19) w.NavigateToNotchView(tab == NotchView.Media ? NotchView.Secondary : NotchView.Media);
                }, ct, 32);
            await Film(host.Window, "music-swipe", i =>
            {
                if (i == 0 || i == 18) { startSwipe = DateTime.UtcNow; w.BeginShellGestureTracking(new Point(), startSwipe); }
                int k = i < 18 ? i : i - 18;
                if (k is >= 1 and <= 4) w.UpdateGestureTracking(new Point(-k * 20, 0), startSwipe.AddMilliseconds(k * 40));
                if (k == 5) { w.FinishMusicSwipe(false, startSwipe.AddMilliseconds(160)); MusicMotionTests.Apply(w, MusicMotionTests.Track(i < 18 ? "Second track" : "Third track", i < 18 ? Colors.Teal : Colors.SlateBlue)); }
                if (i == 8) { w.BeginMusicSkip(-1, 300); MusicMotionTests.Apply(w, MusicMotionTests.Track("Reversed swipe", Colors.SteelBlue)); }
            }, ct);
        }
        finally { AnimationConfig.SetReduceMotion(before); AnimationConfig.ClientAreaAnimationOverride = animations; }
    }, 180);
    private DateTime startSwipe;

    [Fact]
    public void DrawersAndSchool() => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool before = AnimationConfig.ReduceMotion;
        bool? animations = AnimationConfig.ClientAreaAnimationOverride;
        AnimationConfig.ClientAreaAnimationOverride = true;
        try
        {
            foreach (var page in new[] { ControlCenterPage.Pi, ControlCenterPage.School, ControlCenterPage.System })
            {
                using var client = new PiDashboardClient(() => "http://127.0.0.1:1");
                using var view = page == ControlCenterPage.System ? SystemTabTests.CreateView() : new ControlCenterView { Page = page };
                Set(view, "_piRefreshing", true); Set(view, "_refreshing", true);
                if (page != ControlCenterPage.System)
                {
                    view.AttachPi(client, () => false, () => false, () => "", () => "");
                    if (page == ControlCenterPage.Pi)
                    {
                        string json = (string)typeof(HomeSchoolTabTests).GetField("HomeJson", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
                        Set(view, "_home", PiDashboardClient.ParseHome((JsonObject)JsonNode.Parse(json)!));
                        Call(view, "BuildHome");
                    }
                    else
                    {
                        var lesson = new PiLesson("08:00", "08:50", "AM", "E58", false, "", 480, 530, "0AM");
                        var school = new PiSchool(DateTime.Today.ToString("yyyy-MM-dd"), true, new[] { lesson }, new[] { new PiDeadline("Math assignment", "AM", DateTimeOffset.Now.AddDays(2), false) }, Array.Empty<PiExam>(), null)
                        { Days = new Dictionary<string, IReadOnlyList<PiLesson>> { [DateTime.Today.ToString("yyyy-MM-dd")] = new[] { lesson }, [DateTime.Today.AddDays(1).ToString("yyyy-MM-dd")] = new[] { lesson with { Subject = "English" } } } };
                        Set(view, "_piSchool", school); Call(view, "BuildPiPage");
                    }
                }
                var root = new Border { Background = Brushes.Black, Padding = new Thickness(24, 12, 24, 12), Child = view };
                var host = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Width = page == ControlCenterPage.School ? 450 : 600, Height = 440, Content = root };
                try
                {
                    host.Show(); host.UpdateLayout(); AnimationConfig.SetReduceMotion(false);
                    await Film(host, "drawer-" + page, i => { if (i == 0 || i == 25) view.SetDrawerOpen(true); if (i == 13 || i == 27) view.SetDrawerOpen(false); }, ct, 44);
                    await Film(host, "drawer-drag-" + page, i => { if (i is >= 1 and <= 5) view.MoveDrawer(i * 15); if (i == 6) view.ReleaseDrawer(375, false); if (i == 9) view.SetDrawerOpen(false); }, ct);
                    if (page == ControlCenterPage.School)
                    {
                        AnimationConfig.SetReduceMotion(true); view.SetDrawerOpen(true); AnimationConfig.SetReduceMotion(false);
                        await Film(host, "school-timetable", i => { if (i == 0 || i == 25 || i == 27) Call(view, "SelectSchoolDay", DateTime.Today); if (i == 13) Call(view, "SelectSchoolDay", DateTime.Today.AddDays(1)); }, ct, 44);
                    }
                }
                finally { host.Close(); }
            }
        }
        finally { AnimationConfig.SetReduceMotion(before); AnimationConfig.ClientAreaAnimationOverride = animations; }
    }, 120);

    [Fact]
    public void PillsAndHuds() => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool before = AnimationConfig.ReduceMotion;
        AnimationConfig.SetReduceMotion(false);
        try
        {
            foreach (string surface in new[] { "AppVolume", "SystemVolume", "Brightness", "PowerMode", "MicMute", "Bluetooth", "BluetoothDisconnect", "Charging", "AppNotification", "Claude", "Copied", "ChargeOrb", "IdleGlance", "Timer" })
            {
                await using var fixture = MusicMotionTests.Create(); var w = fixture.Window;
                using var host = new Host(w, 720, 200);
                w.SetDebugViewState("CompactMusicPill");
                w.BluetoothDeviceName.Text = "Headphones"; w.BluetoothDisconnectDeviceName.Text = "Headphones";
                void Open()
                {
                    if (surface == "MicMute") { Call(w, "OnMicMuteChanged", false); Call(w, "OnMicMuteChanged", true); }
                    else if (surface == "PowerMode") Call(w, "OnPowerModeChanged", LegionMode.Performance);
                    else if (Enum.GetNames(typeof(MainWindow).GetNestedType("LevelHudKind", BindingFlags.NonPublic)!).Contains(surface))
                        Call(w, "ShowLevelIndicator", .62f, Enum.Parse(typeof(MainWindow).GetNestedType("LevelHudKind", BindingFlags.NonPublic)!, surface));
                    else if (surface.StartsWith("Bluetooth")) { Set(w, "_currentBluetoothConnected", surface == "Bluetooth"); Call(w, "AnimateBluetoothNotificationIn", surface == "Bluetooth"); }
                    else if (surface == "Timer") Call(w, "ShowCountdownCompletionOnPill", (object?)null);
                    else if (surface == "Charging") Call(w, "ShowChargingGlance", new BatteryInfo { Percentage = 64, IsPluggedIn = true, IsCharging = true }, Enum.ToObject(typeof(MainWindow).GetNestedType("ChargingGlanceKind", BindingFlags.NonPublic)!, 0));
                    else if (surface == "AppNotification") Call(w, "ShowAppNotificationPill", new WindowsNotification(1, "Download", null, "Ready", "Your file is available", null, Array.Empty<string>(), null), null, 0);
                    else if (surface == "ChargeOrb") { Call(w, "UpdateChargeOrb", new BatteryInfo { Percentage = 64 }); Call(w, "UpdateChargeOrb", new BatteryInfo { Percentage = 64, IsPluggedIn = true, IsCharging = true }); }
                    else if (surface == "IdleGlance") Call(w, "ShowIdleGlance", true);
                    else { var arbiter = Get<CompactPillArbiter>(w, "_compactPillArbiter"); Set(w, "_claudeNotificationToken", arbiter.TryAcquire(CompactPillSlot.Claude).Token); Set(w, "_isClaudeNotificationVisible", true); w.ClaudeNotificationTitle.Text = surface == "Copied" ? "Copied" : "Build complete"; w.ClaudeNotificationStatus.Text = "Motion study"; Call(w, "AnimateClaudeNotificationIn"); Call(w, "FitNotchToPill"); }
                }
                void Close()
                {
                    if (surface == "Timer") Call(w, "CountdownDismiss_Click", w, new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                    else if (surface.StartsWith("Bluetooth")) Call(w, "AnimateBluetoothNotificationOut");
                    else if (surface == "Charging") Call(w, "DismissChargingNotification");
                    else if (surface == "AppNotification") Call(w, "DismissAppPill");
                    else if (surface == "ChargeOrb") Call(w, "UpdateChargeOrb", new BatteryInfo { Percentage = 64 });
                    else if (surface == "IdleGlance") Call(w, "ShowIdleGlance", false);
                    else if (surface is "Copied" or "Claude") Call(w, "AnimateClaudeNotificationOut");
                    else Call(w, "HideVolumeIndicator");
                }
                await Task.Delay(300, ct);
                await Film(host.Window, "pill-" + surface, i => { if (i == 0 || i == 25) Open(); if (i == 13 || i == 27) Close(); if (i == 29) Open(); }, ct, 44);
            }
        }
        finally { AnimationConfig.SetReduceMotion(before); }
    }, 180);

    [Fact]
    public void Phone() => SharedStaTestRunner.RunAsync(async ct =>
    {
        var w = new PhoneMirrorWindow(() => null) { Left = -20000, Top = -20000, Width = 80, Height = 20 };
        var slot = Get<Grid>(w, "_screenSlot");
        slot.Children.Add(new Image { Source = MusicMotionTests.Artwork(Colors.SlateBlue), Stretch = Stretch.UniformToFill });
        var start = new Int32Rect(-20000, -20000, 80, 20);
        var end = new Int32Rect(-20050, -19980, 180, 360);
        try
        {
            w.Show();
            await Film(w, "phone", i => { if (i == 0 || i == 25) Call(w, "Spring", start, end, Motion.Pop, null); if (i == 13 || i == 27) Call(w, "Spring", end, start, Motion.PopBack, null); }, ct, 44);
        }
        finally { w.Close(); }
    }, 60);

    [Fact]
    public void Spotlight() => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool before = AnimationConfig.ReduceMotion; AnimationConfig.SetReduceMotion(false);
        using var fixture = new SpotlightWindowFixture(new NotchSettings());
        var w = fixture.Window; w.Left = w.Top = -20000;
        try
        {
            await Film(w, "spotlight", i => { if (i == 0 || i == 25) { w.ShowSpotlight(); w.Left = w.Top = -20000; } if (i == 13 || i == 27) w.HideSpotlight(); }, ct, 44);
        }
        finally { AnimationConfig.SetReduceMotion(before); }
    }, 60);

    private sealed class Host : IDisposable
    {
        private readonly MainWindow owner;
        internal Window Window { get; }
        internal Host(MainWindow w, double width, double height)
        {
            owner = w; var content = w.Content; w.Content = null;
            Motion.Stop(w.NotchBorder, FrameworkElement.WidthProperty, 300); Motion.Stop(w.NotchBorder, FrameworkElement.HeightProperty, 40);
            w.CollapsedContent.Visibility = Visibility.Visible;
            Window = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Width = width, Height = height, Content = content };
            Window.Show(); Window.UpdateLayout();
        }
        public void Dispose() { var content = Window.Content; Window.Content = null; Window.Close(); owner.Content = content; }
    }
}




