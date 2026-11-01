using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace VNotch.Services.ControlCenter;

internal enum WallpaperEngineState
{
    NotInstalled,
    NotRunning,
    Running,
    Paused,
    Stopped
}

/// <summary>
/// Drives Wallpaper Engine through its documented command line (<c>wallpaper32.exe -control …</c>),
/// so the tray menu never has to be opened. Wallpaper Engine has no way to report whether it is
/// paused, so that part of the state is what we last told it.
/// </summary>
internal sealed class WallpaperEngineControl
{
    private const string LogTag = "WALLPAPER-ENGINE";
    private const string RegistryKey = @"SOFTWARE\WallpaperEngine";
    private static readonly string[] EngineProcessNames = ["wallpaper32", "wallpaper64"];
    // The engine's own helpers; only ones started from the install folder are closed on quit.
    private static readonly string[] HelperProcessNames = ["ui32", "webwallpaper32", "launcher"];

    private int _trackedProcessId;
    private bool _paused;
    private bool _stopped;
    private bool _muted;

    public bool IsMuted => _muted;

    public WallpaperEngineState GetState()
    {
        int pid = FindRunningProcessId(out _);
        if (pid == 0)
        {
            _trackedProcessId = 0;
            _paused = _stopped = _muted = false;
            return FindExecutable() == null ? WallpaperEngineState.NotInstalled : WallpaperEngineState.NotRunning;
        }

        if (pid != _trackedProcessId)
        {
            // A fresh start plays and has sound again.
            _trackedProcessId = pid;
            _paused = _stopped = _muted = false;
        }

        if (_stopped) return WallpaperEngineState.Stopped;
        return _paused ? WallpaperEngineState.Paused : WallpaperEngineState.Running;
    }

    public bool Pause() => Control("pause", () => _paused = true);

    public bool Play() => Control("play", () => { _paused = false; _stopped = false; });

    public bool Stop() => Control("stop", () => _stopped = true);

    public bool ToggleMute() => _muted
        ? Control("unmute", () => _muted = false)
        : Control("mute", () => _muted = true);

    public bool NextWallpaper() => Control("nextWallpaper", () => { _paused = false; _stopped = false; });

    public bool Start()
    {
        string? exe = FindExecutable();
        if (exe == null) return false;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(LogTag, $"Start failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Wallpaper Engine has no quit command: stop the wallpaper first so Windows shows its own
    /// background again, then close the engine and its helpers.
    /// </summary>
    public async Task QuitAsync()
    {
        int pid = FindRunningProcessId(out string? exe);
        if (pid == 0) return;
        Control("stop", () => _stopped = true);
        await Task.Delay(1200).ConfigureAwait(false);

        string? folder = exe != null ? Path.GetDirectoryName(exe) : null;
        foreach (var name in EngineProcessNames) KillProcesses(name, folder);
        foreach (var name in HelperProcessNames) KillProcesses(name, folder);
        _trackedProcessId = 0;
        _paused = _stopped = _muted = false;
    }

    private bool Control(string command, Action onSent)
    {
        FindRunningProcessId(out string? exe);
        exe ??= FindExecutable();
        if (exe == null) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, "-control " + command)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe)
            });
            onSent();
            return true;
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(LogTag, $"'{command}' failed: {ex.Message}");
            return false;
        }
    }

    private static int FindRunningProcessId(out string? executable)
    {
        executable = null;
        foreach (var name in EngineProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        executable = process.MainModule?.FileName;
                    }
                    catch (Exception)
                    {
                        // Path is a convenience; the registry still knows where the engine lives.
                    }
                    return process.Id;
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        return 0;
    }

    /// <summary>The engine's executable, as Wallpaper Engine records it at install time.</summary>
    internal static string? FindExecutable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            if (key?.GetValue("installPath") is string path && File.Exists(path)) return path;
        }
        catch (Exception)
        {
            // Unreadable registry: fall through to the default Steam location.
        }

        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam", "steamapps", "common", "wallpaper_engine", "wallpaper32.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    private static void KillProcesses(string name, string? folder)
    {
        var processes = Process.GetProcessesByName(name);
        foreach (var process in processes)
        {
            try
            {
                if (folder != null)
                {
                    string? path = null;
                    try { path = process.MainModule?.FileName; } catch (Exception) { /* Access denied: leave it alone */ }
                    if (path == null || !path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) continue;
                }
                process.Kill();
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn(LogTag, $"Couldn't close {name}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}
