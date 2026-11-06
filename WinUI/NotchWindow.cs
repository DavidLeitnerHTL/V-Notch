using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VNotch.WinUI.Composition;
using VNotch.WinUI.Media;
using VNotch.WinUI.Motion;
using VNotch.WinUI.Views;
using Windows.Graphics;
using Windows.Storage.Streams;
using static VNotch.WinUI.Composition.Anim;
using static VNotch.WinUI.Native;

namespace VNotch.WinUI;

/// <summary>
/// The notch window. It is a transparent, top-most tool window hanging from the top of the chosen monitor; only
/// the notch shape takes the mouse. Shape, shadow, glow, clip, cover and fades all run on the compositor, so they
/// move at the monitor's refresh rate whatever the UI thread is doing.
/// </summary>
internal sealed class NotchWindow : Window
{
    private const float WinW = 640, WinH = 200;
    private static readonly Vector2 OpenedSize = new(480, 147);
    private const float OpenedRadius = 24, IdleWidth = 150, HoverScale = 1.08f;

    private readonly NotchSettings _settings = NotchSettings.Load();
    private readonly IntPtr _hwnd;
    private readonly Compositor _c;
    private readonly NotchShell _shell;
    private readonly Visual _compactVisual, _mediaVisual, _playbackVisual, _rightVisual;
    private readonly CompactView _compact;
    private readonly MediaView _media;
    private readonly CoverArt _cover;
    private readonly MediaSource _source = new();
    private readonly DispatcherQueueTimer _pointerTimer, _fullscreenTimer, _clockTimer, _progressTimer, _collapseTimer;
    private RECT _monitor;
    private double _scale = 1;
    private bool _open, _hovered, _clickThrough = true, _hasMedia, _hidden, _settling;
    private int _generation, _coverVersion;

    public NotchWindow()
    {
        SystemBackdrop = new WinUIEx.TransparentTintBackdrop();

        var root = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        var host = new Grid();
        var shadowHost = new Grid { IsHitTestVisible = false };
        var bodyHost = new Grid { IsHitTestVisible = false };
        var glowHost = new Grid { IsHitTestVisible = false };
        var content = new Grid();
        var coverHost = new Grid { IsHitTestVisible = false };
        _compact = new CompactView(IdleWidth, _settings.Height);
        _media = new MediaView();
        _media.Root.Margin = new Thickness((WinW - MediaView.Width) / 2, 0, 0, 0);
        _media.Root.IsHitTestVisible = false;
        content.Children.Add(_compact.Root);
        content.Children.Add(_media.Root);
        content.Children.Add(coverHost);
        host.Children.Add(shadowHost);
        host.Children.Add(bodyHost);
        host.Children.Add(glowHost);
        host.Children.Add(content);
        root.Children.Add(host);
        Content = root;

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = "V-Notch (WinUI)";
        int corner = 1; // DWMWCP_DONOTROUND
        DwmSetWindowAttribute(_hwnd, 33, ref corner, 4);
        int noBorder = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(_hwnd, 34, ref noBorder, 4);
        SetWindowLongW(_hwnd, GWL_EXSTYLE, GetWindowLongW(_hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
        RemoveFrame(_hwnd);
        PlaceOnMonitor();

        _c = ElementCompositionPreview.GetElementVisual(root).Compositor;
        _shell = new NotchShell(_c, WinW, WinH, ClosedSize, (float)_settings.CornerRadius);
        _shell.Props.InsertScalar("H", 1);
        ElementCompositionPreview.SetElementChildVisual(shadowHost, _shell.Shadow);
        ElementCompositionPreview.SetElementChildVisual(bodyHost, _shell.Body);
        ElementCompositionPreview.SetElementChildVisual(glowHost, _shell.Glow);

        // The hover stretch scales the whole notch about its top centre, as the WPF NotchScale does.
        var hostVisual = ElementCompositionPreview.GetElementVisual(host);
        hostVisual.CenterPoint = new Vector3(WinW / 2, 0, 0);
        Bind(hostVisual, "Scale", "Vector3(p.H, 1, 1)", ("p", _shell.Props));

        // Content is clipped by the same shape, so it can never stick out of or lag behind the edge.
        ElementCompositionPreview.GetElementVisual(content).Clip = _c.CreateGeometricClip(_shell.CreateGeometry());

        _cover = new CoverArt(_c);
        ElementCompositionPreview.SetElementChildVisual(coverHost, _cover.Visual);

        foreach (var element in new UIElement[] { _compact.Root, _media.Root, _media.Playback, _media.RightColumn })
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        _compactVisual = ElementCompositionPreview.GetElementVisual(_compact.Root);
        _mediaVisual = ElementCompositionPreview.GetElementVisual(_media.Root);
        _playbackVisual = ElementCompositionPreview.GetElementVisual(_media.Playback);
        _rightVisual = ElementCompositionPreview.GetElementVisual(_media.RightColumn);
        _mediaVisual.Opacity = 0;
        Bind(_compactVisual, "CenterPoint", "Vector3(this.Target.Size.X / 2, this.Target.Size.Y / 2, 0)");
        Bind(_rightVisual, "CenterPoint", "Vector3(this.Target.Size.X / 2, this.Target.Size.Y / 2, 0)");

        root.PointerPressed += OnPointerPressed;
        _media.PreviousRequested += _source.Previous;
        _media.PlayPauseRequested += _source.TogglePlayPause;
        _media.NextRequested += _source.Next;
        _source.TrackChanged += () => DispatcherQueue.TryEnqueue(OnTrackChanged);
        _source.PlaybackChanged += () => DispatcherQueue.TryEnqueue(OnPlaybackChanged);

        _pointerTimer = Timer(15, OnPointerTick);
        _fullscreenTimer = Timer(1000, OnFullscreenTick);
        _clockTimer = Timer(1000, UpdateClock);
        _progressTimer = Timer(250, UpdateProgress);
        _collapseTimer = Timer(_settings.HoverCollapseDelayMs, Collapse);
        _collapseTimer.IsRepeating = false;
        _pointerTimer.Start();
        _fullscreenTimer.Start();
        UpdateClock();
        _clockTimer.Start();
        StartMedia();

        // Testing aid: VNOTCH_WINUI_AUTOTOGGLE=<ms> opens and closes the notch on its own, so its motion can be
        // filmed without moving the user's mouse.
        if (double.TryParse(Environment.GetEnvironmentVariable("VNOTCH_WINUI_AUTOTOGGLE"), out double toggleMs) && toggleMs >= 200)
            Timer(toggleMs, () => { if (_open) Collapse(); else Expand(); }).Start();
    }

    private Vector2 ClosedSize => new((float)(_hasMedia ? _settings.Width : Math.Min(_settings.Width, IdleWidth)), (float)_settings.Height);

    private CoverRect CompactCover => new((WinW - ClosedSize.X) / 2 + 8, ((float)_settings.Height - 22) / 2, 22, 6);

    private static CoverRect OpenCover => new((WinW - OpenedSize.X) / 2 + MediaView.CoverX, MediaView.CoverY, MediaView.CoverSize, MediaView.CoverRadius);

    private DispatcherQueueTimer Timer(double ms, Action tick)
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(ms);
        timer.Tick += (_, _) => tick();
        return timer;
    }

    private void PlaceOnMonitor()
    {
        (_monitor, _scale) = FindMonitor(_settings.MonitorDeviceId);
        int w = (int)Math.Round(WinW * _scale), h = (int)Math.Round(WinH * _scale);
        int x = _monitor.Left + (_monitor.Right - _monitor.Left - w) / 2;
        AppWindow.MoveAndResize(new RectInt32(x, _monitor.Top, w, h));
        // A frameless window can keep a thin top border; lift it so the client area meets the screen edge.
        var origin = new POINT();
        ClientToScreen(_hwnd, ref origin);
        int lift = origin.Y - _monitor.Top;
        if (lift > 0) AppWindow.MoveAndResize(new RectInt32(x, _monitor.Top - lift, w, h + lift));
    }

    private async void StartMedia()
    {
        try
        {
            await _source.StartAsync();
        }
        catch (Exception e)
        {
            App.Log(e);
        }
    }

    // ── Open and close ───────────────────────────────────────────────────────────────────────────

    private void Expand()
    {
        if (_open) return;
        _open = true;
        ++_generation;
        _settling = false;
        _collapseTimer.Stop();

        var props = _shell.Props;
        props.StartAnimation("S", Spring(_c, OpenedSize, Springs.OpenResponse, Springs.OpenBounce));
        props.StartAnimation("R", Spring(_c, OpenedRadius, Springs.OpenResponse, 0));
        props.StartAnimation("H", Spring(_c, 1f, Springs.OpenResponse, Springs.OpenBounce));

        // As in the WPF notch: the closed content fades in 200 ms, the media page comes in over 500 ms sliding up
        // 10 DIP, then the playback row (+20 ms) and the right column (+60 ms) follow, 25 ms after the start.
        Run(_compactVisual, "Opacity", null, 0, 200, Easings.QuadOut);
        Run(_mediaVisual, "Opacity", null, 1, 500, Easings.AppleOut);
        Run(_mediaVisual, "Translation.Y", 10, 0, 400, Easings.ExpOut6);
        Run(_playbackVisual, "Opacity", 0, 1, 340, Easings.AppleOut, 45);
        Run(_playbackVisual, "Translation.Y", 10, 0, 340, Easings.ExpOut6, 45);
        Run(_rightVisual, "Opacity", 0, 1, 380, Easings.AppleOut, 85);
        Run(_rightVisual, "Translation.Y", 12, 0, 380, Easings.ExpOut6, 85);
        Run(_rightVisual, "Scale.X", 0.96f, 1, 380, Easings.ExpOut6, 85);
        Run(_rightVisual, "Scale.Y", 0.96f, 1, 380, Easings.ExpOut6, 85);

        if (_hasMedia)
        {
            _cover.ResetScale();
            _cover.Show(true);
            _cover.MorphTo(OpenCover);
        }
        else
        {
            _cover.Place(OpenCover);
            _cover.Reveal(25);
        }
        if (_settings.GlowOnHover) Run(_shell.Glow, "Opacity", null, 0.15f, 200, Easings.Linear);

        _media.Root.IsHitTestVisible = true;
        _media.SetBattery(BatteryPercent());
        UpdateProgress();
        _progressTimer.Start();
    }

    private void Collapse()
    {
        if (!_open) return;
        _open = false;
        int generation = ++_generation;
        _collapseTimer.Stop();
        _progressTimer.Stop();
        _media.Root.IsHitTestVisible = false;

        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        var props = _shell.Props;
        props.StartAnimation("S", Spring(_c, ClosedSize, Springs.CloseResponse, Springs.CloseBounce));
        props.StartAnimation("R", Spring(_c, (float)_settings.CornerRadius, Springs.CloseResponse, 0));

        // WPF: the page fades over 500 ms on ExpOut6 sliding up 10 DIP; the closed content returns over 400 ms,
        // growing from 80 % when it is the idle clock; the thumbnail flies back after 30 ms.
        Run(_mediaVisual, "Opacity", null, 0, 500, Easings.ExpOut6);
        Run(_mediaVisual, "Translation.Y", null, -10, 400, Easings.ExpOut6);
        Run(_compactVisual, "Opacity", null, 1, 400, Easings.PowerOut3);
        if (!_hasMedia)
        {
            Run(_compactVisual, "Scale.X", 0.8f, 1, 400, Easings.SoftSpring);
            Run(_compactVisual, "Scale.Y", 0.8f, 1, 400, Easings.SoftSpring);
            _cover.FadeOut(200);
        }
        else
        {
            _cover.MorphTo(CompactCover, 30);
        }
        Run(_shell.Glow, "Opacity", null, 0, 150, Easings.Linear);
        batch.End();

        _settling = true;
        batch.Completed += (_, _) =>
        {
            if (generation != _generation) return;
            _settling = false;
            if (_hovered) SetHover(true);
        };
    }

    // ── Hover, clicks and click-through ─────────────────────────────────────────────────────────

    /// <summary>WPF AnimateNotchHover: stretch to 108 % on the soft spring (500 ms), back on QuadOut (350 ms).</summary>
    private void SetHover(bool on)
    {
        if (_open || _settling) return;
        if (on) Run(_shell.Props, "H", null, HoverScale, 500, Easings.SoftSpring);
        else Run(_shell.Props, "H", null, 1, 350, Easings.QuadOut);
        if (_settings.GlowOnHover) Run(_shell.Glow, "Opacity", null, on ? 0.35f : 0, 200, Easings.QuadOut);
    }

    private void OnPointerTick()
    {
        GetCursorPos(out var cursor);
        var origin = new POINT();
        ClientToScreen(_hwnd, ref origin);
        double x = (cursor.X - origin.X) / _scale, y = (cursor.Y - origin.Y) / _scale;
        var size = _open ? OpenedSize : ClosedSize;
        bool inside = HitTest.InsideNotch(x, y, WinW, size.X, size.Y, !_open && _hovered ? HoverScale : 1);

        if (inside == _clickThrough)
        {
            _clickThrough = !inside;
            int ex = GetWindowLongW(_hwnd, GWL_EXSTYLE);
            SetWindowLongW(_hwnd, GWL_EXSTYLE, _clickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
        }
        if (inside != _hovered)
        {
            _hovered = inside;
            OnHoverChanged(inside);
        }

        // Look closely only while the cursor is near the notch.
        bool near = x > -40 && x < WinW + 40 && y > -40 && y < WinH + 40;
        var interval = TimeSpan.FromMilliseconds(near ? 15 : 120);
        if (_pointerTimer.Interval != interval) _pointerTimer.Interval = interval;
    }

    private void OnHoverChanged(bool inside)
    {
        if (!_open)
        {
            SetHover(inside);
            return;
        }
        if (_settings.DisableMouseLeaveAutoClose) return;
        if (inside) _collapseTimer.Stop();
        else _collapseTimer.Start();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint((UIElement)sender).Properties;
        if (point.IsRightButtonPressed)
        {
            // Test build: right-click quits, so it is easy to switch back to the WPF notch.
            Application.Current.Exit();
            return;
        }
        if (!point.IsLeftButtonPressed) return;
        if (_open) Collapse();
        else Expand();
    }

    public void ShowWithoutFocus()
    {
        AppWindow.Show(false);
        KeepOnTop(_hwnd);
    }

    private void OnFullscreenTick()
    {
        bool fullscreen = IsFullscreenOn(_monitor, _hwnd);
        // The presenter's always-on-top flag does not stick for a never-activated tool window; assert it.
        if (!fullscreen) KeepOnTop(_hwnd);
        if (fullscreen == _hidden) return;
        _hidden = fullscreen;
        if (fullscreen)
        {
            Collapse();
            _pointerTimer.Stop();
            AppWindow.Hide();
        }
        else
        {
            ShowWithoutFocus();
            _pointerTimer.Start();
        }
    }

    // ── Content ─────────────────────────────────────────────────────────────────────────────────

    private async void OnTrackChanged()
    {
        bool had = _hasMedia;
        _hasMedia = _source.HasTrack;
        _media.SetTrack(_source.Title, _source.Artist);
        _compact.Root.Visibility = _hasMedia ? Visibility.Collapsed : Visibility.Visible;
        OnPlaybackChanged();

        if (had != _hasMedia && !_open)
        {
            // The closed notch widens for a track and slims down without one.
            _shell.Props.StartAnimation("S", Spring(_c, ClosedSize, Springs.CloseResponse, Springs.CloseBounce));
            _cover.ResetScale();
            _cover.Place(CompactCover);
            if (_hasMedia) Run(_cover.Visual, "Opacity", 0, 1, 280, Easings.AppleOut, 120);
            else _cover.Show(false);
        }

        int version = ++_coverVersion;
        IRandomAccessStream? stream = null;
        try
        {
            if (_source.Thumbnail is { } thumbnail) stream = await thumbnail.OpenReadAsync();
        }
        catch (Exception e)
        {
            App.Log(e);
        }
        if (version == _coverVersion) _cover.SetImage(stream);
    }

    private void OnPlaybackChanged()
    {
        _media.SetPlaying(_source.IsPlaying);
        if (_open) UpdateProgress();
    }

    private void UpdateProgress() => _media.SetProgress(_source.Position, _source.Duration);

    private void UpdateClock()
    {
        var now = DateTime.Now;
        _compact.SetClock(now);
        _media.SetClock(now);
        _clockTimer.Interval = TimeSpan.FromMilliseconds((60 - now.Second) * 1000 - now.Millisecond + 20);
    }

    private static int? BatteryPercent()
    {
        try
        {
            return Windows.System.Power.PowerManager.BatteryStatus == Windows.System.Power.BatteryStatus.NotPresent
                ? null
                : Windows.System.Power.PowerManager.RemainingChargePercent;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
