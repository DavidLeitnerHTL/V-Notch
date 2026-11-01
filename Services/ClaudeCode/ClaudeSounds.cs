using System;
using System.IO;
using System.Runtime.InteropServices;

namespace VNotch.Services.ClaudeCode;

internal enum ClaudeSound
{
    /// <summary>A turn ended.</summary>
    Done,
    /// <summary>A session waits for an approval, an answer or a plan decision.</summary>
    Waiting
}

/// <summary>Windows' own short notification sounds, played without blocking and never on top of each other.</summary>
internal static class ClaudeSounds
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_FILENAME = 0x00020000;
    private static readonly TimeSpan MinGap = TimeSpan.FromSeconds(2);
    private static DateTime _lastPlayedUtc = DateTime.MinValue;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(string sound, IntPtr module, uint flags);

    public static void Play(ClaudeSound sound)
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastPlayedUtc < MinGap) return;
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media",
            sound == ClaudeSound.Done ? "Windows Notify Messaging.wav" : "Windows Proximity Notification.wav");
        if (!File.Exists(file)) return;
        _lastPlayedUtc = now;
        PlaySound(file, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
    }
}
