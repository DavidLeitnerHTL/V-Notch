using System;
using System.Runtime.InteropServices;
using System.Text;
using VNotch.Services;

namespace VNotch;

public partial class MainWindow
{
    #region Windows' own volume and brightness pill

    // Windows answers the volume and brightness keys with its own pill at the bottom of the screen: one explorer
    // window (XamlExplorerHostIslandWindow, no-activate tool window, topmost) that it shows again on every press.
    // While the notch shows that same change in its HUD, the pill is hidden the moment it appears. Over a
    // fullscreen game or with the notch open the notch shows nothing, so Windows' pill stays.
    private const uint EventObjectShow = 0x8002;
    private const int SwHide = 0;
    private const string WindowsOsdClass = "XamlExplorerHostIslandWindow";
    private const int WindowsOsdExStyle =
        Win32Interop.WS_EX_NOACTIVATE | Win32Interop.WS_EX_TOOLWINDOW | Win32Interop.WS_EX_TOPMOST;
    // The key press reaches Windows' pill and the notch in either order; this pairs them.
    private static readonly TimeSpan WindowsOsdPairing = TimeSpan.FromMilliseconds(1500);

    private Win32Interop.WinEventHook? _windowShownHook;
    private IntPtr _windowsOsd;
    private DateTime _levelHudShownUtc = DateTime.MinValue;

    private void InitializeWindowsOsdHider()
    {
        _windowShownHook = new Win32Interop.WinEventHook(EventObjectShow, EventObjectShow, OnWindowShown);
        if (!_windowShownHook.Install()) RuntimeLog.Warn("LEVEL-HUD", "Windows' volume pill cannot be hidden: no show hook");
    }

    private void DisposeWindowsOsdHider()
    {
        _windowShownHook?.Dispose();
        _windowShownHook = null;
    }

    /// <summary>Any window anywhere was shown; only Windows' level pill matters.</summary>
    private void OnWindowShown(uint eventType, IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _cleanedUp) return;
        if (hwnd != _windowsOsd)
        {
            if (!IsWindowsOsd(hwnd)) return;
            _windowsOsd = hwnd;
        }
        if (IsShowingSystemLevel()) HideWindowsOsd();
    }

    /// <summary>Right after the notch showed a system volume or brightness change.</summary>
    private void HideWindowsOsdForLevelHud()
    {
        _levelHudShownUtc = DateTime.UtcNow;
        if (_windowsOsd == IntPtr.Zero || !IsWindow(_windowsOsd)) _windowsOsd = FindWindowsOsd();
        if (_windowsOsd != IntPtr.Zero && Win32Interop.IsWindowVisible(_windowsOsd)) HideWindowsOsd();
    }

    private bool IsShowingSystemLevel() =>
        _isVolumeIndicatorActive && _levelHudKind != LevelHudKind.AppVolume &&
        DateTime.UtcNow - _levelHudShownUtc < WindowsOsdPairing;

    private void HideWindowsOsd() => Win32Interop.ShowWindow(_windowsOsd, SwHide);

    private static bool IsWindowsOsd(IntPtr hwnd)
    {
        if ((Win32Interop.GetWindowLong(hwnd, Win32Interop.GWL_EXSTYLE) & WindowsOsdExStyle) != WindowsOsdExStyle) return false;
        var name = new StringBuilder(64);
        return Win32Interop.GetClassName(hwnd, name, name.Capacity) > 0 &&
               string.Equals(name.ToString(), WindowsOsdClass, StringComparison.Ordinal);
    }

    /// <summary>Explorer keeps the pill's window between presses; the other windows of that class fill the screen.</summary>
    private static IntPtr FindWindowsOsd()
    {
        IntPtr hwnd = IntPtr.Zero;
        for (int i = 0; i < 32; i++)
        {
            hwnd = FindWindowEx(IntPtr.Zero, hwnd, WindowsOsdClass, null);
            if (hwnd == IntPtr.Zero) return IntPtr.Zero;
            if (IsWindowsOsd(hwnd)) return hwnd;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hwnd);

    #endregion
}
