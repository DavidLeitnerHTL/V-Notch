using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using VNotch.Services;

namespace VNotch;

/// <summary>
/// Testing aids for Tools/NotchMotion, off unless their environment variable is set:
/// VNOTCH_AUTOTOGGLE=&lt;ms&gt; opens and closes the notch on its own, so its motion can be filmed without moving
/// the user's mouse; VNOTCH_INTERRUPT_MS=&lt;ms&gt; closes each opening again after that delay.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _autoToggleTimer;

    private void StartMotionLab()
    {
        if (Environment.GetEnvironmentVariable("VNOTCH_EXP_PRUNE_BLUR") == "1")
            RuntimeLog.Log("MOTION-LAB", $"detached {DetachIdleBlurs(this)} idle blur effects");
        if (double.TryParse(Environment.GetEnvironmentVariable("VNOTCH_EXP_CACHE"), NumberStyles.Float, CultureInfo.InvariantCulture, out double cacheScale) && cacheScale > 0)
        {
            // Experiment: render the media page once into a bitmap, so a fade or slide only recomposes it.
            foreach (var element in new UIElement[] { ExpandedContent, MusicCompactContent, CollapsedContent })
                element.CacheMode = new BitmapCache(cacheScale) { SnapsToDevicePixels = true };
        }

        if (!double.TryParse(Environment.GetEnvironmentVariable("VNOTCH_AUTOTOGGLE"), NumberStyles.Float, CultureInfo.InvariantCulture, out double toggleMs) || toggleMs < 200)
            return;
        bool interrupt = double.TryParse(Environment.GetEnvironmentVariable("VNOTCH_INTERRUPT_MS"), NumberStyles.Float, CultureInfo.InvariantCulture, out double interruptMs) && interruptMs > 0;
        var closeAgain = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interrupt ? interruptMs : 1000) };
        closeAgain.Tick += (_, _) => { closeAgain.Stop(); CollapseNotch(); };
        _autoToggleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(toggleMs) };
        _autoToggleTimer.Tick += (_, _) =>
        {
            if (_isExpanded) { CollapseNotch(); return; }
            ExpandNotch();
            if (interrupt) closeAgain.Start();
        };
        _autoToggleTimer.Start();
    }

    // Experiment: a plain (not layered) window made see-through by DWM, as WinUI does it. WPF then presents with
    // the GPU instead of copying every frame back to memory for UpdateLayeredWindow. Must run before the window
    // gets its handle.
    private void ApplyDwmTransparencyExperiment()
    {
        if (Environment.GetEnvironmentVariable("VNOTCH_EXP_DWM") != "1") return;
        AllowsTransparency = false;
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            IntPtr hwnd = source.Handle;
            var region = CreateRectRgn(-2, -2, -1, -1);
            var blur = new DwmBlurBehind { Flags = 0x1 | 0x2, Enable = true, BlurRegion = region };
            DwmEnableBlurBehindWindow(hwnd, ref blur);
            int noRound = 1, noBorder = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(hwnd, 33, ref noRound, 4);
            DwmSetWindowAttribute(hwnd, 34, ref noBorder, 4);
            RuntimeLog.Log("MOTION-LAB", "DWM transparency instead of a layered window");
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DwmBlurBehind { public int Flags; public bool Enable; public IntPtr BlurRegion; public bool TransitionOnMaximized; }
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DwmBlurBehind blur);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    // Experiment: blur effects that sit at radius 0 still route their element through an effect pass every frame.
    private static int DetachIdleBlurs(DependencyObject root)
    {
        int count = 0;
        if (root is UIElement element && element.Effect is BlurEffect blur && blur.Radius == 0 && !blur.HasAnimatedProperties)
        {
            element.Effect = null;
            count++;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            count += DetachIdleBlurs(VisualTreeHelper.GetChild(root, i));
        return count;
    }
}
