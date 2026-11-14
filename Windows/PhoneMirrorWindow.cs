using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VNotch.Services;
using VNotch.Services.Phone;

namespace VNotch;

/// <summary>Where the closed notch sits, in device pixels of its screen.</summary>
internal readonly record struct NotchAnchor(int CenterX, int Bottom, int Width, double Scale, Int32Rect WorkArea);

/// <summary>
/// The phone's screen hanging from the notch: a black phone body that springs out of the notch, with scrcpy's
/// picture inside. Dragged away it becomes a free window; dropped near the notch it docks again. A click on the
/// notch (or the close button) pulls it back in.
/// </summary>
internal sealed class PhoneMirrorWindow : Window
{
    private const double ScreenShare = 0.62;
    private const double Bezel = 7;
    private const double GripHeight = 20;
    private const double BottomBezel = 9;
    private const double DockDistance = 90;
    private static readonly TimeSpan KeepWarm = TimeSpan.FromSeconds(30);

    private readonly Func<NotchAnchor?> _anchor;
    private readonly Grid _screenSlot = new() { Background = Brushes.Black };
    private readonly Border _close;
    private readonly DispatcherTimer _coolDown;
    private ScrcpyProcess? _scrcpy;
    private ScrcpyChild? _child;
    private int _cornerRadius;
    private Task<bool>? _ensuring;
    private CancellationTokenSource? _startCancel;
    private string? _startingSerial;
    private double _aspect = 1200.0 / 2608.0;
    private EventHandler? _frame;
    private IntPtr _hwnd;

    public PhoneMirrorWindow(Func<NotchAnchor?> anchor)
    {
        _anchor = anchor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Black;
        Title = "V-Notch Handy";
        SizeToContent = SizeToContent.Manual;

        var grip = new Grid { Height = GripHeight, Background = Brushes.Black, Cursor = Cursors.SizeAll };
        grip.Children.Add(new Border
        {
            Width = 34, Height = 4, CornerRadius = new CornerRadius(2),
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x44)),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        });
        _close = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 4, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x31)), Cursor = Cursors.Hand, Opacity = 0,
            Child = new TextBlock
            {
                Text = "\uE711", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 8,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD4)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            }
        };
        _close.MouseLeftButtonUp += (_, e) => { e.Handled = true; PullIn(); };
        _close.MouseLeftButtonDown += (_, e) => e.Handled = true;
        grip.Children.Add(_close);
        grip.MouseEnter += (_, _) => FadeClose(1);
        grip.MouseLeave += (_, _) => FadeClose(0);
        grip.MouseLeftButtonDown += Grip_MouseLeftButtonDown;

        _screenSlot.Margin = new Thickness(Bezel, 0, Bezel, BottomBezel);
        _screenSlot.SizeChanged += (_, _) => PlaceChild();
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(grip);
        Grid.SetRow(_screenSlot, 1);
        layout.Children.Add(_screenSlot);
        Content = layout;

        _coolDown = new DispatcherTimer(DispatcherPriority.Background) { Interval = KeepWarm };
        _coolDown.Tick += (_, _) =>
        {
            _coolDown.Stop();
            if (State == MirrorState.Hidden) StopScrcpy();
        };

        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(_hwnd, 33, ref round, sizeof(int));
    }

    public enum MirrorState { Hidden, Docked, Free }

    public MirrorState State { get; private set; } = MirrorState.Hidden;
    public bool IsOpen => State != MirrorState.Hidden;
    public string? Serial => _scrcpy?.Serial ?? _startingSerial;

    /// <summary>Raised when the mirror is fully pulled back in or scrcpy ended.</summary>
    public event Action? MirrorClosed;

    /// <summary>The phone's screen size in pixels, for the body's proportions.</summary>
    public void SetPhoneSize(int width, int height)
    {
        if (width > 0 && height > 0) _aspect = (double)Math.Min(width, height) / Math.Max(width, height);
    }

    /// <summary>Starts scrcpy ahead of a likely tap, so opening is instant; it stops again if nobody opens it.</summary>
    public void Prewarm(string serial)
    {
        if (State != MirrorState.Hidden) return;
        _ = EnsureScrcpyAsync(serial);
        _coolDown.Stop();
        _coolDown.Start();
    }

    public async void Open(string serial)
    {
        try { await OpenAsync(serial); }
        catch (Exception ex)
        {
            // Never let the mirror take the notch down with it.
            RuntimeLog.Warn("PHONE", $"Mirror failed: {ex.GetType().Name}: {ex.Message}");
            Shutdown();
            MirrorClosed?.Invoke();
        }
    }

    private async Task OpenAsync(string serial)
    {
        if (State != MirrorState.Hidden || _anchor() is not { } anchor) return;
        _coolDown.Stop();
        State = MirrorState.Docked;
        Topmost = true;
        var target = DockedRect(anchor);
        var start = TuckedRect(anchor);
        PlaceNow(start);
        SetScreenVisible(false);
        Show();
        // The body grows right away; the picture joins as soon as scrcpy is up (at once when it was warm).
        var ready = EnsureScrcpyAsync(serial);
        bool grown = false;
        Spring(start, target, stiffness: 300, damping: 21, done: () =>
        {
            grown = true;
            if (ready.IsCompletedSuccessfully && ready.Result) RevealScreen();
        });
        bool ok = await ready;
        if (State == MirrorState.Hidden) return;
        if (!ok)
        {
            PullIn();
            return;
        }
        if (grown) RevealScreen();
    }

    /// <summary>Pulls the mirror back into the notch; scrcpy stays warm for a little while.</summary>
    public void PullIn()
    {
        if (State == MirrorState.Hidden) return;
        State = MirrorState.Hidden;
        SetScreenVisible(false);
        var anchor = _anchor();
        if (anchor == null || !IsVisible)
        {
            Hide();
            MirrorClosed?.Invoke();
            return;
        }
        Topmost = true;
        Spring(CurrentRect(), TuckedRect(anchor.Value), stiffness: 520, damping: 46, done: () =>
        {
            Hide();
            _coolDown.Stop();
            _coolDown.Start();
            MirrorClosed?.Invoke();
        });
    }

    /// <summary>Stops everything: on phone disconnect and when the notch quits.</summary>
    public void Shutdown()
    {
        StopFrame();
        State = MirrorState.Hidden;
        Hide();
        StopScrcpy();
    }

    private Task<bool> EnsureScrcpyAsync(string serial)
    {
        if (_scrcpy is { HasExited: false } running && running.Serial == serial) return Task.FromResult(true);
        if (_ensuring != null && _startingSerial == serial) return _ensuring;
        return _ensuring = StartScrcpyAsync(serial);
    }

    private async Task<bool> StartScrcpyAsync(string serial)
    {
        StopScrcpy();
        var anchor = _anchor();
        double scale = anchor?.Scale ?? 1;
        var size = ScreenSize(anchor);
        var cancel = _startCancel = new CancellationTokenSource();
        _startingSerial = serial;
        ScrcpyProcess? started;
        try
        {
            started = await ScrcpyProcess.StartAsync(serial, (int)(size.Width * scale), (int)(size.Height * scale), cancel.Token);
        }
        finally
        {
            _ensuring = null;
            _startingSerial = null;
        }
        if (started == null) return false;
        if (cancel.IsCancellationRequested)
        {
            started.Dispose();
            return false;
        }
        // Hidden until the body has grown around it.
        var child = await ScrcpyChild.AdoptAsync(started.Window, _hwnd);
        if (child == null || cancel.IsCancellationRequested)
        {
            started.Dispose();
            return false;
        }
        _scrcpy = started;
        _child = child;
        _cornerRadius = (int)Math.Round(size.Width * 0.075 * scale);
        started.Exited += () => Dispatcher.BeginInvoke(() =>
        {
            if (_scrcpy != started) return;
            RuntimeLog.Info("PHONE", "scrcpy ended");
            Shutdown();
            MirrorClosed?.Invoke();
        });
        return true;
    }

    private void StopScrcpy()
    {
        _startCancel?.Cancel();
        _startCancel = null;
        _child?.Show(false);
        _child = null;
        _scrcpy?.Dispose();
        _scrcpy = null;
    }

    private void RevealScreen()
    {
        SetScreenVisible(true);
        Activate();
    }

    private void SetScreenVisible(bool visible)
    {
        if (_child == null) return;
        if (visible) PlaceChild();
        _child.Show(visible);
    }

    /// <summary>Lays scrcpy's window over the screen slot, in this window's device pixels.</summary>
    private void PlaceChild()
    {
        if (_child == null || _screenSlot.ActualWidth <= 0 || Content is not UIElement root) return;
        var source = PresentationSource.FromVisual(this);
        double scale = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        Point at = _screenSlot.TranslatePoint(new Point(0, 0), root);
        _child.Place((int)Math.Round(at.X * scale), (int)Math.Round(at.Y * scale),
            (int)Math.Round(_screenSlot.ActualWidth * scale), (int)Math.Round(_screenSlot.ActualHeight * scale), _cornerRadius);
    }

    private void FadeClose(double to) =>
        _close.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(to, TimeSpan.FromMilliseconds(140)));

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (State == MirrorState.Hidden || _frame != null) return;
        e.Handled = true;
        try { DragMove(); }
        catch (InvalidOperationException) { return; }
        if (_anchor() is not { } anchor) return;
        var now = CurrentRect();
        double centerX = now.X + now.Width / 2.0;
        double distance = Math.Sqrt(Math.Pow(centerX - anchor.CenterX, 2) + Math.Pow(now.Y - anchor.Bottom, 2));
        if (distance <= DockDistance * anchor.Scale)
        {
            State = MirrorState.Docked;
            Topmost = true;
            Spring(now, DockedRect(anchor), stiffness: 380, damping: 26, done: null);
        }
        else
        {
            State = MirrorState.Free;
            Topmost = false;
        }
    }

    private Size ScreenSize(NotchAnchor? anchor)
    {
        double scale = anchor?.Scale ?? 1;
        double workHeight = (anchor?.WorkArea.Height ?? 1040) / scale;
        double height = Math.Round(workHeight * ScreenShare);
        return new Size(Math.Round(height * _aspect), height);
    }

    private Int32Rect DockedRect(NotchAnchor anchor)
    {
        var screen = ScreenSize(anchor);
        int width = (int)Math.Round((screen.Width + Bezel * 2) * anchor.Scale);
        int height = (int)Math.Round((screen.Height + GripHeight + BottomBezel) * anchor.Scale);
        // Tucked a few pixels under the notch so the two read as one shape.
        int top = anchor.Bottom - (int)Math.Round(2 * anchor.Scale);
        return new Int32Rect(anchor.CenterX - width / 2, top, width, height);
    }

    private static Int32Rect TuckedRect(NotchAnchor anchor)
    {
        int width = Math.Max(40, (int)(anchor.Width * 0.8));
        int height = (int)Math.Round(6 * anchor.Scale);
        return new Int32Rect(anchor.CenterX - width / 2, anchor.Bottom - height, width, height);
    }

    private Int32Rect CurrentRect()
    {
        GetWindowRect(_hwnd, out var r);
        return new Int32Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private void PlaceNow(Int32Rect rect) =>
        SetWindowPos(_hwnd, IntPtr.Zero, rect.X, rect.Y, Math.Max(1, rect.Width), Math.Max(1, rect.Height), SWP_NOZORDER | SWP_NOACTIVATE);

    /// <summary>
    /// Moves the window along damped springs: height leads, width follows a touch softer, so the body
    /// stretches out of the notch like a drop and settles with a small overshoot.
    /// </summary>
    private void Spring(Int32Rect from, Int32Rect to, double stiffness, double damping, Action? done)
    {
        StopFrame();
        if (AnimationConfig.ReduceMotion)
        {
            PlaceNow(to);
            done?.Invoke();
            return;
        }
        double[] value = [from.X + from.Width / 2.0, from.Y, from.Width, from.Height];
        double[] goal = [to.X + to.Width / 2.0, to.Y, to.Width, to.Height];
        double[] velocity = new double[4];
        double[] k = [stiffness, stiffness, stiffness * 0.8, stiffness];
        var clock = Stopwatch.StartNew();
        double last = 0;
        _frame = (_, _) =>
        {
            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.032, now - last);
            last = now;
            bool settled = true;
            // Small fixed steps keep the spring stable on slow frames.
            for (double left = dt; left > 0; left -= 0.004)
            {
                double step = Math.Min(0.004, left);
                for (int i = 0; i < 4; i++)
                {
                    double force = -k[i] * (value[i] - goal[i]) - damping * velocity[i];
                    velocity[i] += force * step;
                    value[i] += velocity[i] * step;
                }
            }
            for (int i = 0; i < 4; i++)
                if (Math.Abs(value[i] - goal[i]) > 0.5 || Math.Abs(velocity[i]) > 8) settled = false;
            if (settled || now > 1.6)
            {
                StopFrame();
                PlaceNow(to);
                done?.Invoke();
                return;
            }
            int width = (int)Math.Round(value[2]), height = (int)Math.Round(value[3]);
            PlaceNow(new Int32Rect((int)Math.Round(value[0] - width / 2.0), (int)Math.Round(value[1]), width, height));
        };
        CompositionTarget.Rendering += _frame;
    }

    private void StopFrame()
    {
        if (_frame != null) CompositionTarget.Rendering -= _frame;
        _frame = null;
    }

    protected override void OnClosed(EventArgs e)
    {
        Shutdown();
        base.OnClosed(e);
    }

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
