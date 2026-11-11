using System;
using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace VNotch.Services;

/// <summary>
/// The playing track's cover colours as a small file (%LOCALAPPDATA%\V-Notch\cover-colors.json), for the Govee
/// strip's music sync ("Govee PC-Sync" on this PC), which lights the room in the cover's colours instead of
/// running through the rainbow. Written only when the colours change; emptied when nothing plays.
/// </summary>
internal static class CoverColorsFile
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "V-Notch", "cover-colors.json");
    private static string? _last;

    public static void Publish(Color main, Color sub) => Write(new[] { Hex(main), Hex(sub) });

    public static void Clear() => Write(Array.Empty<string>());

    private static void Write(string[] colors)
    {
        string json = JsonSerializer.Serialize(new { colors });
        if (json == _last) return;
        _last = json;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _last = null;
            RuntimeLog.Warn("COVER-COLORS", ex.Message);
        }
    }

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
