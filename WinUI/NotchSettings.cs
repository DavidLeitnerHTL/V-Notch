using System.Text.Json;

namespace VNotch.WinUI;

/// <summary>The few values this notch reads from the WPF notch's settings.json. It never writes the file.</summary>
internal sealed record NotchSettings(
    double Width,
    double Height,
    double CornerRadius,
    string? MonitorDeviceId,
    bool GlowOnHover,
    bool DisableMouseLeaveAutoClose,
    double HoverCollapseDelayMs,
    bool CoverBackdrop,
    double CoverBackdropBoost)
{
    public static NotchSettings Load()
    {
        var defaults = new NotchSettings(230, 34, 8, null, true, false, 500, true, 1.0);
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "V-Notch", "settings.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            return new NotchSettings(
                Number(root, "Width", defaults.Width),
                Number(root, "Height", defaults.Height),
                Number(root, "CornerRadius", defaults.CornerRadius),
                root.TryGetProperty("MonitorDeviceId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null,
                Flag(root, "EnableGlowOnHover", defaults.GlowOnHover),
                Flag(root, "DisableMouseLeaveAutoClose", defaults.DisableMouseLeaveAutoClose),
                Number(root, "HoverCollapseDelay", defaults.HoverCollapseDelayMs),
                Flag(root, "ShowMediaArtBackground", true) && Flag(root, "EnableBlurEffects", true),
                Number(root, "MediaBlurBrightnessBoost", defaults.CoverBackdropBoost));
        }
        catch (Exception)
        {
            return defaults;
        }
    }

    private static double Number(JsonElement root, string name, double fallback) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    private static bool Flag(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
}
