using VNotch.Services;

namespace VNotch;

/// <summary>
/// The notch window is see-through by DWM composition rather than by WPF's layered window, with clicks falling
/// through wherever nothing visible is under the cursor (see <see cref="DwmTransparentWindow"/>).
/// </summary>
public partial class MainWindow
{
    /// <summary>Must run before the window gets its handle (right after InitializeComponent).</summary>
    private void InitializeTransparency() => DwmTransparentWindow.Apply(this);

    // Also used by the OSD hiders (MainWindow.WindowsOsd, MainWindow.LenovoOsd) for their empty regions.
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern System.IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(System.IntPtr handle);
}
