using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace VNotch.Services.Phone;

/// <summary>
/// One running scrcpy whose window lives inside ours. scrcpy (C, SDL, hardware decode) is the fastest mirror there is;
/// it starts off-screen and is adopted as a child window, so it looks like part of the notch and never flashes as a
/// window of its own. Starting takes about 0.7 s (server push, encoder start, first frame), which is why the mirror
/// starts one ahead of a likely tap and keeps it warm for a while.
/// </summary>
internal sealed class ScrcpyProcess : IDisposable
{
    private const string SdlWindowClass = "SDL_app";
    private static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(8);

    private readonly Process _process;
    private int _disposed;

    private ScrcpyProcess(Process process, string serial, IntPtr window)
    {
        _process = process;
        Serial = serial;
        Window = window;
    }

    public string Serial { get; }
    public IntPtr Window { get; }
    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }
    }

    public event Action? Exited;

    /// <summary>Starts scrcpy for the phone and waits for its window; null when it did not come up.</summary>
    public static async Task<ScrcpyProcess?> StartAsync(string serial, int width, int height, CancellationToken ct)
    {
        if (PhoneTools.Scrcpy is not { } exe) return null;
        string title = $"V-Notch Handy {Environment.ProcessId} {Guid.NewGuid():N}";
        var info = PhoneTools.StartInfo(exe,
        [
            "-s", serial,
            "--window-borderless",
            $"--window-title={title}",
            // Off-screen until it is adopted: it never shows up as a window of its own.
            "--window-x=-32000", "--window-y=-32000",
            $"--window-width={width}", $"--window-height={height}",
            "--no-audio",
            "--video-codec=h264"
        ]);
        Process? process;
        try { process = Process.Start(info); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            RuntimeLog.Warn("PHONE", $"scrcpy did not start: {ex.Message}");
            return null;
        }
        if (process == null) return null;
        ProcessJob.Add(process);
        // Its output has to be read or it blocks once the pipe is full; only errors matter.
        process.OutputDataReceived += (_, e) => LogLine(e.Data);
        process.ErrorDataReceived += (_, e) => LogLine(e.Data);
        if (info.RedirectStandardOutput) process.BeginOutputReadLine();
        if (info.RedirectStandardError) process.BeginErrorReadLine();

        var clock = Stopwatch.StartNew();
        IntPtr window = IntPtr.Zero;
        while (clock.Elapsed < WindowWait && !ct.IsCancellationRequested)
        {
            window = FindWindowW(SdlWindowClass, title);
            if (window != IntPtr.Zero || process.HasExited) break;
            try { await Task.Delay(15, ct); }
            catch (TaskCanceledException) { break; }
        }
        if (window == IntPtr.Zero)
        {
            Kill(process);
            process.Dispose();
            if (!ct.IsCancellationRequested) RuntimeLog.Warn("PHONE", "scrcpy window did not appear");
            return null;
        }
        RuntimeLog.Info("PHONE", $"scrcpy window after {clock.ElapsedMilliseconds} ms");
        var started = new ScrcpyProcess(process, serial, window);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => started.Exited?.Invoke();
        return started;
    }

    private static void LogLine(string? line)
    {
        if (line != null && line.Contains("ERROR", StringComparison.Ordinal)) RuntimeLog.Warn("PHONE", line.Trim());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Kill(_process);
        _process.Dispose();
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    private static extern IntPtr FindWindowW(string? className, string? title);
}

/// <summary>
/// Puts scrcpy's own window into a WPF layout as a child window. WPF sizes it with the layout; the corners are
/// rounded like a phone screen with a window region.
/// </summary>
internal sealed class ScrcpyWindowHost : HwndHost
{
    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;

    private readonly IntPtr _child;
    private readonly double _cornerRadius;

    public ScrcpyWindowHost(IntPtr child, double cornerRadius)
    {
        _child = child;
        _cornerRadius = cornerRadius;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        int style = GetWindowLongW(_child, GWL_STYLE);
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME);
        style |= WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS;
        SetWindowLongW(_child, GWL_STYLE, style);
        SetParent(_child, hwndParent.Handle);
        return new HandleRef(this, _child);
    }

    // The window belongs to scrcpy: it goes away when scrcpy is stopped, not when WPF lets go of the host.
    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        SetParent(_child, IntPtr.Zero);
    }

    protected override void OnWindowPositionChanged(System.Windows.Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        int width = (int)Math.Round(rcBoundingBox.Width), height = (int)Math.Round(rcBoundingBox.Height);
        if (width <= 0 || height <= 0) return;
        var source = PresentationSourceScale();
        int radius = (int)Math.Round(_cornerRadius * source * 2);
        IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
        if (SetWindowRgn(_child, region, true) == 0) DeleteObject(region);
    }

    private double PresentationSourceScale()
    {
        var source = System.Windows.PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(IntPtr hwnd, int index);
    [DllImport("user32.dll")]
    private static extern int SetWindowLongW(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}

/// <summary>A job object that takes scrcpy along when the notch ends, even when it crashes.</summary>
internal static class ProcessJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private static readonly Lazy<IntPtr> Job = new(CreateJob);

    public static void Add(Process process)
    {
        IntPtr job = Job.Value;
        if (job == IntPtr.Zero) return;
        try { AssignProcessToJobObject(job, process.Handle); }
        catch (InvalidOperationException) { }
    }

    private static IntPtr CreateJob()
    {
        IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        var info = new JobExtendedLimits { Basic = new JobBasicLimits { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE } };
        int size = Marshal.SizeOf<JobExtendedLimits>();
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size))
                RuntimeLog.Warn("PHONE", "Job object limits not set");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobExtendedLimits
    {
        public JobBasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
}
