using System;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace VNotch.Services.ControlCenter;

/// <summary>The Wi-Fi and Bluetooth switches from Windows' quick settings.</summary>
internal static class RadioSwitch
{
    private const string LogTag = "RADIO";

    /// <summary>The PC's radio of that kind, or null when it has none (or Windows won't say).</summary>
    public static async Task<Radio?> FindAsync(RadioKind kind)
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            foreach (var radio in radios)
            {
                if (radio.Kind == kind) return radio;
            }
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(LogTag, $"{kind} radio unavailable: {ex.Message}");
        }
        return null;
    }

    /// <summary>Switches the radio on or off, like the tile in quick settings. False if Windows didn't let us.</summary>
    public static async Task<bool> SetAsync(Radio radio, bool on)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return false;
            return await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(LogTag, $"Couldn't switch {radio.Kind}: {ex.Message}");
            return false;
        }
    }
}
