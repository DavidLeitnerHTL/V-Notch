using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
using VNotch.Services;
using static VNotch.Services.Win32Interop;

namespace VNotch.Controllers;

/// <summary>
/// Lets Spotlight stand in for the Windows Start menu: a lone tap of a Windows key and a left click on
/// the taskbar's Start button are taken away from Windows and reported instead. Win+key combinations,
/// right clicks on Start (the Win+X menu) and Ctrl+Esc stay with Windows. Both low-level hooks live on
/// one dedicated thread so a busy UI thread can never stall system input.
/// </summary>
internal sealed unsafe partial class StartMenuHooks : IDisposable
{
    private const string LogTag = "START-REPLACEMENT";
    // Marks the keys we inject so our own hook lets them through untouched.
    private static readonly nint InjectionMarker = 0x564E5354;
    private const ushort MaskKey = 0xE8; // unassigned: Windows sees Win+<nothing useful> and keeps Start closed
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const int WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204, WmMButtonDown = 0x0207, WmXButtonDown = 0x020B;
    private const long StartRefreshThrottleMs = 200;
    private const long TaskbarRectRefreshMs = 1000;

    private static StartMenuHooks? s_mouseOwner;

    private readonly Action _winTap;
    private readonly Action _startClick;
    private readonly WinKeyTapDetector _detector = new();
    private readonly Thread _thread;
    private readonly LowLevelKeyboardHook _keyboard;
    private Dispatcher? _dispatcher;
    private nint _mouseHook;
    private bool _swallowStartUp;
    private int _disposed;

    // Start button and taskbar bounds in physical screen pixels, refreshed off the hook thread.
    private RECT _startRect;
    private RECT _taskbarRect;
    private long _taskbarRectAt;
    private long _startRefreshAt;
    private int _startRefreshRunning;

    internal bool IsInstalled { get; private set; }

    /// <param name="winTap">Called on the hook thread after a lone Win tap was taken from Windows.</param>
    /// <param name="startClick">Called on the hook thread after a left click on Start was taken from Windows.</param>
    internal StartMenuHooks(Action winTap, Action startClick)
    {
        _winTap = winTap;
        _startClick = startClick;
        _keyboard = new LowLevelKeyboardHook(OnKey);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Start menu replacement hooks" };
        _thread.Start();
        ready.Task.GetAwaiter().GetResult();
        if (IsInstalled) QueueStartRefresh(force: true);
    }

    private void Run(TaskCompletionSource ready)
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            bool keyboard = _keyboard.Install();
            if (Interlocked.CompareExchange(ref s_mouseOwner, this, null) == null)
            {
                delegate* unmanaged[Stdcall]<int, nint, nint, nint> proc = &OnMouse;
                _mouseHook = SetWindowsHookExW(WhMouseLl, (nint)proc, GetModuleHandleW(null), 0);
                if (_mouseHook == 0) Interlocked.CompareExchange(ref s_mouseOwner, null, this);
            }
            IsInstalled = keyboard;
            if (!keyboard) RuntimeLog.Warn(LogTag, $"Keyboard hook failed (Win32={_keyboard.InstallationError})");
            if (_mouseHook == 0) RuntimeLog.Warn(LogTag, "Mouse hook for the Start button could not be installed");
            ready.TrySetResult();
            if (keyboard || _mouseHook != 0) Dispatcher.Run();
        }
        catch (Exception ex)
        {
            RuntimeLog.Error(LogTag, ex, "Start menu replacement hooks failed");
        }
        finally
        {
            ready.TrySetResult();
            RemoveHooks();
        }
    }

    private void RemoveHooks()
    {
        _keyboard.Dispose();
        if (_mouseHook != 0)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
        Interlocked.CompareExchange(ref s_mouseOwner, null, this);
    }

    private bool OnKey(KBDLLHOOKSTRUCT key, int message)
    {
        if (Volatile.Read(ref _disposed) != 0 || key.dwExtraInfo == InjectionMarker) return false;
        bool down = message is WmKeyDown or WmSysKeyDown;
        bool up = message is WmKeyUp or WmSysKeyUp;
        if (!down && !up) return false;
        if (_detector.Process(key.vkCode, down) != WinKeyTapAction.Tap) return false;

        // Swallow the real Win-up and replay it behind an unassigned key, so Windows treats the
        // press as a combination and never opens Start.
        SendMaskedWinRelease((ushort)key.vkCode);
        _winTap();
        return true;
    }

    private static void SendMaskedWinRelease(ushort winVk)
    {
        const uint keyUp = 0x0002, extended = 0x0001;
        var inputs = stackalloc INPUT[3];
        inputs[0] = KeyInput(MaskKey, 0);
        inputs[1] = KeyInput(MaskKey, keyUp);
        inputs[2] = KeyInput(winVk, keyUp | extended);
        SendInput(3, inputs, sizeof(INPUT));
    }

    private static INPUT KeyInput(ushort vk, uint flags) => new()
    {
        type = 1,
        ki = new KEYBDINPUT { wVk = vk, dwFlags = flags, dwExtraInfo = InjectionMarker },
    };

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint OnMouse(int code, nint wParam, nint lParam)
    {
        try
        {
            var owner = Volatile.Read(ref s_mouseOwner);
            if (code >= 0 && owner != null && owner.HandleMouse((int)wParam, (MSLLHOOKSTRUCT*)lParam)) return 1;
        }
        catch
        {
            // Never let an exception unwind into user32's input chain.
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private bool HandleMouse(int message, MSLLHOOKSTRUCT* data)
    {
        // Injected clicks count too (automation tools, tests); this hook never injects mouse input itself.
        if (Volatile.Read(ref _disposed) != 0) return false;
        switch (message)
        {
            case WmMouseMove:
                if (IsOverTaskbar(data->pt)) QueueStartRefresh(force: false);
                return false;
            case WmLButtonDown:
                _detector.NoteOtherInput();
                if (Contains(_startRect, data->pt) && IsOverTaskbar(data->pt))
                {
                    _swallowStartUp = true;
                    _startClick();
                    return true;
                }
                return false;
            case WmLButtonUp:
                if (!_swallowStartUp) return false;
                _swallowStartUp = false;
                return true;
            case WmRButtonDown or WmMButtonDown or WmXButtonDown:
                _detector.NoteOtherInput();
                return false;
            default:
                return false;
        }
    }

    private bool IsOverTaskbar(POINT pt)
    {
        long now = Environment.TickCount64;
        if (now - _taskbarRectAt > TaskbarRectRefreshMs)
        {
            _taskbarRectAt = now;
            IntPtr tray = FindWindow("Shell_TrayWnd");
            _taskbarRect = tray != IntPtr.Zero && GetWindowRect(tray, out RECT rect) ? rect : default;
        }
        return Contains(_taskbarRect, pt);
    }

    private static bool Contains(RECT r, POINT pt) =>
        r.Right > r.Left && pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;

    private void QueueStartRefresh(bool force)
    {
        long now = Environment.TickCount64;
        if (!force && now - Volatile.Read(ref _startRefreshAt) < StartRefreshThrottleMs) return;
        if (Interlocked.Exchange(ref _startRefreshRunning, 1) != 0) return;
        Volatile.Write(ref _startRefreshAt, now);
        // UI Automation calls can take tens of milliseconds; never make the input hook wait for them.
        _ = Task.Run(() =>
        {
            try { _startRect = LocateStartButton() ?? default; }
            catch (Exception ex) { RuntimeLog.Log(LogTag, $"Start button lookup failed: {ex.Message}"); }
            finally { Volatile.Write(ref _startRefreshRunning, 0); }
        });
    }

    private static RECT? LocateStartButton()
    {
        IntPtr tray = FindWindow("Shell_TrayWnd");
        if (tray == IntPtr.Zero) return null;
        var taskbar = AutomationElement.FromHandle(tray);
        var start = taskbar.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton"));
        if (start == null) return null;
        var bounds = start.Current.BoundingRectangle;
        if (bounds.IsEmpty || bounds.Width < 1) return null;
        return new RECT
        {
            Left = (int)Math.Floor(bounds.Left),
            Top = (int)Math.Floor(bounds.Top),
            Right = (int)Math.Ceiling(bounds.Right),
            Bottom = (int)Math.Ceiling(bounds.Bottom),
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var dispatcher = _dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.Invoke(() =>
        {
            RemoveHooks();
            dispatcher.InvokeShutdown();
        }, DispatcherPriority.Send);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    // The union in INPUT is as large as MOUSEINPUT; KEYBDINPUT shares its start.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int idHook, nint lpfn, nint hmod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? lpModuleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint cInputs, INPUT* pInputs, int cbSize);
}
