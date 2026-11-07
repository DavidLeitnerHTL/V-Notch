using System;
using System.Globalization;
using System.Windows.Threading;

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
}
