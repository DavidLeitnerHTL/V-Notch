using System;
using System.Runtime.InteropServices;

namespace VNotch.Services.ControlCenter;

/// <summary>
/// Mutes the default microphone, the one voice chat uses unless told otherwise. Core Audio's endpoint volume,
/// declared here because the native audio layer only covers the capture level.
/// </summary>
internal static class MicrophoneControl
{
    private const int DataFlowCapture = 1;
    private const int RoleConsole = 0;
    private const int ClsCtxAll = 0x17;
    private static readonly Guid EndpointVolumeId = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>Null when there is no microphone.</summary>
    public static bool? IsMuted()
    {
        var volume = DefaultMicrophone();
        if (volume == null) return null;
        try
        {
            return volume.GetMute(out bool muted) == 0 ? muted : null;
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    public static bool SetMuted(bool mute)
    {
        var volume = DefaultMicrophone();
        if (volume == null) return false;
        try
        {
            var context = Guid.Empty;
            return volume.SetMute(mute, ref context) == 0;
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    private static IAudioEndpointVolume? DefaultMicrophone()
    {
        object? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            // No microphone plugged in: Windows answers "not found" here.
            if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(DataFlowCapture, RoleConsole, out device) != 0 || device == null)
                return null;
            var id = EndpointVolumeId;
            return device.Activate(ref id, ClsCtxAll, IntPtr.Zero, out object endpoint) == 0 ? endpoint as IAudioEndpointVolume : null;
        }
        catch (COMException ex)
        {
            RuntimeLog.Warn("CONTROL-CENTER", $"Microphone not reachable: {ex.Message}");
            return null;
        }
        finally
        {
            if (device != null) Marshal.ReleaseComObject(device);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }
}
