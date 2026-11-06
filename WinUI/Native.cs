using System.Runtime.InteropServices;

namespace VNotch.WinUI;

internal static class Native
{
    public const int GWL_EXSTYLE = -20, GWL_STYLE = -16;
    public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_CAPTION = 0x00C00000;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLongW(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, char[] name, int max);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplayDevicesW(string? device, uint index, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    private static readonly SubclassProc NoFrameProc = (hwnd, msg, wParam, lParam, id, data) =>
        msg == 0x0083 /* WM_NCCALCSIZE */ && wParam != IntPtr.Zero ? IntPtr.Zero : DefSubclassProc(hwnd, msg, wParam, lParam);

    /// <summary>
    /// A frameless OverlappedPresenter window keeps WS_DLGFRAME, and DWM draws that frame as a light line around the
    /// transparent window. Claiming the whole window as client area removes the frame (and its 4 px top strip).
    /// </summary>
    public static void RemoveFrame(IntPtr hwnd)
    {
        SetWindowSubclass(hwnd, NoFrameProc, new UIntPtr(1), UIntPtr.Zero);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020 /* …| NOZORDER | NOACTIVATE | FRAMECHANGED */);
    }

    /// <summary>Puts the window above all non-top-most windows without moving, sizing or activating it.</summary>
    public static void KeepOnTop(IntPtr hwnd) =>
        SetWindowPos(hwnd, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* NOSIZE | NOMOVE | NOACTIVATE */);

    /// <summary>The monitor the WPF notch is set to (settings MonitorDeviceId), else the primary one.</summary>
    public static (RECT Bounds, double Scale) FindMonitor(string? deviceId)
    {
        IntPtr chosen = IntPtr.Zero;
        // Testing aid: VNOTCH_WINUI_SECONDARY=1 puts the notch on a monitor other than the primary one, so it can
        // be tried without covering the WPF notch (or a game on the main screen).
        if (Environment.GetEnvironmentVariable("VNOTCH_WINUI_SECONDARY") == "1")
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                bool primary = rect.Left <= 0 && rect.Top <= 0 && rect.Right > 0 && rect.Bottom > 0;
                if (primary) return true;
                chosen = monitor;
                return false;
            }, IntPtr.Zero);
        }
        if (chosen == IntPtr.Zero && !string.IsNullOrEmpty(deviceId))
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                if (string.Equals(DeviceInterfaceId(monitor), deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    chosen = monitor;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
        }
        if (chosen == IntPtr.Zero) chosen = MonitorFromPoint(new POINT(), 1); // MONITOR_DEFAULTTOPRIMARY
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        GetMonitorInfoW(chosen, ref info);
        double scale = GetDpiForMonitor(chosen, 0, out uint dpi, out _) == 0 ? dpi / 96.0 : 1;
        return (info.rcMonitor, scale);
    }

    private static string? DeviceInterfaceId(IntPtr monitor)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfoW(monitor, ref info)) return null;
        var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        return EnumDisplayDevicesW(info.szDevice, 0, ref dd, 1 /* EDD_GET_DEVICE_INTERFACE_NAME */) ? dd.DeviceID : null;
    }

    /// <summary>A game or video covering the whole monitor: the notch steps aside, as the WPF notch does.</summary>
    public static bool IsFullscreenOn(RECT monitor, IntPtr self)
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == self || fg == GetShellWindow()) return false;
        var name = new char[64];
        int length = GetClassNameW(fg, name, name.Length);
        string cls = new(name, 0, Math.Max(0, length));
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if ((GetWindowLongW(fg, GWL_STYLE) & WS_CAPTION) == WS_CAPTION) return false;
        return GetWindowRect(fg, out var r)
            && r.Left <= monitor.Left && r.Top <= monitor.Top && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
    }
}
