using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace VNotch.Services.ControlCenter;

/// <summary>An app's share of the machine: all its processes added up.</summary>
internal readonly record struct ProcessLoad(string Name, string Title, double CpuPercent, long MemoryBytes, int Instances, bool CanEnd);

/// <summary>
/// The apps using the most CPU or memory in the signed-in session, for the controls tab's CPU and RAM
/// tiles. Load is a difference between two readings, so it only runs while that list is open.
/// </summary>
internal sealed class ProcessLoadSampler
{
    private const int BaselineMs = 300;
    // Windows itself, the shell and what this notch needs to run: ending them breaks the desktop or the session.
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "Idle", "System", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon", "services", "lsass",
        "svchost", "dwm", "explorer", "sihost", "ctfmon", "fontdrvhost", "RuntimeBroker", "ShellExperienceHost",
        "StartMenuExperienceHost", "SearchHost", "TextInputHost", "ApplicationFrameHost", "LockApp", "conhost",
        "OpenConsole", "WindowsTerminal", "V-Notch", "dllhost", "taskhostw"
    };

    private static readonly int OwnSession = Process.GetCurrentProcess().SessionId;
    private readonly Dictionary<string, string> _titles = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, TimeSpan> _lastCpu = new();
    private long _lastTicks;

    /// <summary>The list closed: the next reading takes a fresh baseline.</summary>
    public void Reset() => _lastTicks = 0;

    public List<ProcessLoad> Top(int count, bool byMemory)
    {
        if (_lastTicks == 0)
        {
            Sample();
            Thread.Sleep(BaselineMs);
        }
        var list = new List<ProcessLoad>(Sample().Values);
        Comparison<ProcessLoad> order = byMemory
            ? static (a, b) => b.MemoryBytes.CompareTo(a.MemoryBytes)
            : static (a, b) => b.CpuPercent.CompareTo(a.CpuPercent);
        list.Sort(order);
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
        for (int i = 0; i < list.Count; i++) list[i] = list[i] with { Title = TitleOf(list[i].Name) };
        return list;
    }

    private Dictionary<string, ProcessLoad> Sample()
    {
        long now = Stopwatch.GetTimestamp();
        double seconds = _lastTicks == 0 ? 0 : (now - _lastTicks) / (double)Stopwatch.Frequency;
        double capacity = seconds * Environment.ProcessorCount;
        var cpuNow = new Dictionary<int, TimeSpan>(_lastCpu.Count + 16);
        var byName = new Dictionary<string, ProcessLoad>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != OwnSession || process.Id == 0) continue;
                    TimeSpan cpu = process.TotalProcessorTime;
                    cpuNow[process.Id] = cpu;
                    double share = capacity > 0 && _lastCpu.TryGetValue(process.Id, out var before)
                        ? Math.Max(0, (cpu - before).TotalSeconds) / capacity * 100
                        : 0;
                    string name = process.ProcessName;
                    byName.TryGetValue(name, out var sum);
                    byName[name] = new ProcessLoad(name, name, sum.CpuPercent + share, sum.MemoryBytes + process.WorkingSet64,
                        sum.Instances + 1, !Protected.Contains(name) && process.Id != Environment.ProcessId);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Elevated (no access from here) or already gone.
                }
            }
        }
        _lastCpu = cpuNow;
        _lastTicks = now;
        return byName;
    }

    /// <summary>"Google Chrome" rather than "chrome": the description its program file carries, looked up once.</summary>
    private string TitleOf(string name)
    {
        if (_titles.TryGetValue(name, out var title)) return title;
        title = name;
        var processes = Process.GetProcessesByName(name);
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } description)
                    {
                        title = description.Trim();
                        break;
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Elevated or 32/64-bit mismatch: the name has to do.
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
        _titles[name] = title;
        return title;
    }

    /// <summary>
    /// Ends every process of the app in this session: windows are asked to close first, like Task Manager's
    /// "End task", and what is still running a moment later is stopped.
    /// </summary>
    public static bool End(string name)
    {
        if (Protected.Contains(name)) return false;
        bool any = false;
        var processes = Process.GetProcessesByName(name);
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.SessionId != OwnSession || process.Id == Environment.ProcessId) continue;
                    if (process.MainWindowHandle == IntPtr.Zero || !process.CloseMainWindow() || !process.WaitForExit(1500))
                        process.Kill();
                    any = true;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    RuntimeLog.Warn("CONTROL-CENTER", $"Could not end {name} ({process.Id}): {ex.Message}");
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
        return any;
    }
}
