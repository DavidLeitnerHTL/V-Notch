using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VNotch.Controllers;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

public partial class MainWindow
{
    #region Tapping a notification pill: the app drops out of the notch

    // A tap on a notification pill brings its app up. The app's logo leaves the pill as a drop of the notch: it
    // swells out under the notch on a neck that thins and tears like the Dynamic Island splitting, the notch
    // recoils, and the drop falls toward the window that came up, growing and fading as it gets there.
    // It lives on its own layer behind the notch and runs one frame loop for about 0.65 s, then is gone.
    private const double DropSize = 26;
    private const double DropSplitMs = 260;
    private const double DropEndMs = 700;
    // How far the drop swells out below the notch before it tears free.
    private const double DropHang = 40;

    private Canvas? _dropLayer;
    private Border? _drop;
    private Path? _dropNeck;
    private ScaleTransform? _dropScale;
    private TranslateTransform? _dropMove;
    private EventHandler? _dropFrame;

    private void PlayNotificationDrop(IntPtr foregroundBefore)
    {
        if (AnimationConfig.ReduceMotion || _cleanedUp) return;
        if (NotificationAppIcon.Parent is not FrameworkElement icon || !icon.IsVisible || icon.ActualWidth <= 0) return;
        if (NotchBorder.ActualHeight <= 0) return;
        EnsureDropLayer();
        StopNotificationDrop();

        Point iconCenter = icon.TranslatePoint(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2), NotchContainer);
        double notchBottom = NotchBorder.TranslatePoint(new Point(0, NotchBorder.ActualHeight), NotchContainer).Y;
        double startX = iconCenter.X;

        ((Image)_drop!.Child).Source = SnapshotElement(icon);
        _drop.Opacity = 1;
        _drop.Visibility = Visibility.Visible;
        _dropNeck!.Visibility = Visibility.Visible;
        _dropLayer!.Visibility = Visibility.Visible;

        // The logo left with the drop.
        var gone = MakeAnim(ClaudeNotificationIconScale.ScaleX, 0.0, TimeSpan.FromMilliseconds(140), _easeQuadOut);
        ClaudeNotificationIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, gone);
        ClaudeNotificationIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, gone);
        PlayDropRecoil();

        var clock = Stopwatch.StartNew();
        IntPtr notchHwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        Point? target = null;
        double hangFrom = notchBottom - DropSize * 0.5;
        Point split = new(startX, hangFrom + DropHang);

        _dropFrame = (_, _) =>
        {
            double t = clock.Elapsed.TotalMilliseconds;
            if (target == null && t < DropSplitMs + 60) target = FindDropTarget(foregroundBefore, notchHwnd);

            if (t <= DropSplitMs)
            {
                // Swelling out of the notch, still held by a neck that thins and tears.
                double s = t / DropSplitMs;
                double e = s < 0.5 ? 4 * s * s * s : 1 - Math.Pow(-2 * s + 2, 3) / 2;
                // Starts hidden under the notch (this layer is behind it) and swells out.
                double y = hangFrom + e * DropHang;
                double sx = 1 - 0.12 * e, sy = 1 + 0.22 * e;
                PlaceDrop(startX, y, sx, sy, 1);
                // The neck stays thick while the drop hangs, then thins fast and tears.
                double neck = DropSize * 0.7 * (1 - SmoothStep(0.55, 1.0, s));
                DrawNeck(startX, notchBottom - 3, y, neck, DropSize * sx * 0.72);
                return;
            }

            _dropNeck.Visibility = Visibility.Collapsed;
            double u = Math.Min(1, (t - DropSplitMs) / (DropEndMs - DropSplitMs));
            // Falls toward the app: a curve that starts downward, accelerating like a drop.
            Vector toward = target is { } goal ? goal - split : new Vector(0, 1);
            double distance = Math.Clamp(toward.Length, 90, 240);
            if (toward.Length < 0.001) toward = new Vector(0, 1);
            toward.Normalize();
            Point end = split + toward * distance;
            Point control = split + new Vector(0, distance * 0.45);
            double k = u * u;
            Point at = new(
                (1 - k) * (1 - k) * split.X + 2 * (1 - k) * k * control.X + k * k * end.X,
                (1 - k) * (1 - k) * split.Y + 2 * (1 - k) * k * control.Y + k * k * end.Y);
            // Springs back to round after the tear, and grows as it nears the app.
            double wobble = Math.Exp(-7 * u) * Math.Cos(13 * u);
            double grow = 1 + 0.5 * u;
            double opacity = 1 - SmoothStep(0.3, 1, u);
            PlaceDrop(at.X, at.Y, (1 - 0.12 * wobble) * grow, (1 + 0.18 * wobble) * grow, opacity);
            if (u >= 1) StopNotificationDrop();
        };
        CompositionTarget.Rendering += _dropFrame;
    }

    private void StopNotificationDrop()
    {
        if (_dropFrame != null) CompositionTarget.Rendering -= _dropFrame;
        _dropFrame = null;
        if (_dropLayer != null) _dropLayer.Visibility = Visibility.Collapsed;
        if (_drop?.Child is Image image) image.Source = null;
    }

    private void EnsureDropLayer()
    {
        if (_dropLayer != null) return;
        var notchBrush = (Brush)FindResource("NotchGradient");
        _dropScale = new ScaleTransform(1, 1);
        _dropMove = new TranslateTransform();
        var transforms = new TransformGroup();
        transforms.Children.Add(_dropScale);
        transforms.Children.Add(_dropMove);
        _drop = new Border
        {
            Width = DropSize,
            Height = DropSize,
            CornerRadius = new CornerRadius(DropSize / 2),
            Background = notchBrush,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = transforms,
            Child = new Image { Width = 14, Height = 14, Stretch = Stretch.Uniform },
            IsHitTestVisible = false
        };
        _dropNeck = new Path { Fill = notchBrush, IsHitTestVisible = false };
        _dropLayer = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _dropLayer.Children.Add(_dropNeck);
        _dropLayer.Children.Add(_drop);
        // Behind the notch, so the neck comes out from under it.
        NotchContainer.Children.Insert(NotchContainer.Children.IndexOf(NotchWrapper), _dropLayer);
    }

    private void PlaceDrop(double centerX, double centerY, double scaleX, double scaleY, double opacity)
    {
        _dropMove!.X = centerX - DropSize / 2;
        _dropMove.Y = centerY - DropSize / 2;
        _dropScale!.ScaleX = scaleX;
        _dropScale.ScaleY = scaleY;
        _drop!.Opacity = opacity;
    }

    /// <summary>Two soft curves from the notch's edge, narrowing to the waist and widening onto the drop.</summary>
    private void DrawNeck(double x, double top, double bottom, double waist, double foot)
    {
        if (waist < 0.8 || bottom <= top)
        {
            _dropNeck!.Visibility = Visibility.Collapsed;
            return;
        }
        double mid = (top + bottom) / 2;
        double rim = Math.Max(waist, foot) * 1.15;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(x - rim / 2, top), true, true);
            g.QuadraticBezierTo(new Point(x - waist / 2, mid), new Point(x - foot / 2, bottom), true, true);
            g.LineTo(new Point(x + foot / 2, bottom), true, true);
            g.QuadraticBezierTo(new Point(x + waist / 2, mid), new Point(x + rim / 2, top), true, true);
        }
        geometry.Freeze();
        _dropNeck!.Data = geometry;
        _dropNeck.Visibility = Visibility.Visible;
    }

    /// <summary>The notch gives a little as the drop pulls, and springs back when it tears.</summary>
    private void PlayDropRecoil()
    {
        var recoil = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        recoil.KeyFrames.Add(new EasingDoubleKeyFrame(1.05, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170)), _easeQuadOut));
        recoil.KeyFrames.Add(new EasingDoubleKeyFrame(0.985, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), _easeQuadOut));
        recoil.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)), _easeSoftSpring));
        Timeline.SetDesiredFrameRate(recoil, AnimationConfig.TargetFps);
        NotchScale.BeginAnimation(ScaleTransform.ScaleYProperty, recoil);
    }

    /// <summary>The window that came up, in this layer's coordinates; null while nothing new is in front.</summary>
    private Point? FindDropTarget(IntPtr foregroundBefore, IntPtr notchHwnd)
    {
        IntPtr foreground = OverlayWindowController.GetForegroundWindowHandle();
        if (foreground == IntPtr.Zero || foreground == foregroundBefore || foreground == notchHwnd || IsIconic(foreground)) return null;
        if (!GetWindowRect(foreground, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top) return null;
        try
        {
            return NotchContainer.PointFromScreen(new Point((rect.Left + rect.Right) / 2.0, (rect.Top + rect.Bottom) / 2.0));
        }
        catch (InvalidOperationException)
        {
            return null; // not connected to a presentation source any more
        }
    }

    private static ImageSource? SnapshotElement(FrameworkElement element)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        int w = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX));
        int h = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None }, null,
                new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static double SmoothStep(double from, double to, double x)
    {
        double t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DropRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out DropRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    #endregion
}
