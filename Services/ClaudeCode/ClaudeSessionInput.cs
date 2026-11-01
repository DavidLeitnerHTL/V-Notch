using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VNotch.Services.ClaudeCode;

/// <summary>
/// Types a message into a Claude Code session's prompt, as if typed in its terminal: the keys go into the
/// session's console input, so the terminal does not have to come to the front.
/// </summary>
internal static class ClaudeSessionInput
{
    private const ushort KEY_EVENT = 0x0001;
    private const uint GENERIC_READ_WRITE = 0xC0000000;
    private const uint FILE_SHARE_READ_WRITE = 0x3;
    private const uint OPEN_EXISTING = 3;
    private const ushort VK_RETURN = 0x0D;
    // Claude Code takes a burst of keys ending in Enter for a paste with a line break; Enter on its own submits.
    private static readonly TimeSpan EnterDelay = TimeSpan.FromMilliseconds(150);

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    private struct KeyEventRecord
    {
        [FieldOffset(0)] public int KeyDown;
        [FieldOffset(4)] public ushort RepeatCount;
        [FieldOffset(6)] public ushort VirtualKeyCode;
        [FieldOffset(8)] public ushort VirtualScanCode;
        [FieldOffset(10)] public char UnicodeChar;
        [FieldOffset(12)] public uint ControlKeyState;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputRecord
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KeyEventRecord Key;
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")]
    private static extern bool WriteConsoleInputW(IntPtr input, InputRecord[] records, uint count, out uint written);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char character);
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    /// <summary>False when the session has no console to type into (desktop app, editor) or is gone.</summary>
    public static Task<bool> SendAsync(string sessionId, string message) => Task.Run(() =>
    {
        int processId = ClaudeSessionWindow.FindProcessId(sessionId);
        if (processId == 0) return false;
        if (!Write(processId, Keys(message))) return false;
        Thread.Sleep(EnterDelay);
        return Write(processId, Keys("\r"));
    });

    private static InputRecord[] Keys(string text)
    {
        var records = new InputRecord[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            short scan = character == '\r' ? (short)VK_RETURN : VkKeyScanW(character);
            ushort virtualKey = scan == -1 ? (ushort)0 : (ushort)(scan & 0xFF);
            ushort scanCode = (ushort)MapVirtualKeyW(virtualKey, 0);
            for (int pass = 0; pass < 2; pass++)
            {
                ref var record = ref records[i * 2 + pass];
                record.EventType = KEY_EVENT;
                record.Key.KeyDown = pass == 0 ? 1 : 0;
                record.Key.RepeatCount = 1;
                record.Key.VirtualKeyCode = virtualKey;
                record.Key.VirtualScanCode = scanCode;
                record.Key.UnicodeChar = character;
            }
        }
        return records;
    }

    /// <summary>
    /// Attaches only for the write itself: while attached, closing that terminal would close us too.
    /// </summary>
    private static bool Write(int processId, InputRecord[] records)
    {
        lock (ClaudeSessionWindow.ConsoleGate)
        {
            if (!AttachConsole((uint)processId)) return false;
            try
            {
                IntPtr input = CreateFileW("CONIN$", GENERIC_READ_WRITE, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (input == new IntPtr(-1)) return false;
                try
                {
                    return WriteConsoleInputW(input, records, (uint)records.Length, out uint written) && written == records.Length;
                }
                finally
                {
                    CloseHandle(input);
                }
            }
            finally
            {
                FreeConsole();
            }
        }
    }
}
