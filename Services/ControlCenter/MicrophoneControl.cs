using System;
using System.Collections.Generic;
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

    private static readonly Type EnumeratorType =
        Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!;

    /// <summary>
    /// The device enumerator. Not "new MMDeviceEnumerator()": Windows hands out one enumerator, and when another
    /// part of the app (BluetoothControl) wrapped it first, that new expression would get the other wrapper
    /// type back and throw InvalidCastException. Casting to the interface works whatever the wrapper.
    /// </summary>
    private static object CreateEnumerator() => Activator.CreateInstance(EnumeratorType)!;

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

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, ref Guid context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr events);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint processId);
    }

    private const int DeviceStateActive = 1;
    private const int AudioSessionStateActive = 1;
    private static readonly Guid SessionManagerId = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    /// <summary>
    /// The processes recording right now: an active capture session on any microphone, whether sound comes through
    /// or not (a call keeps recording while the mic is muted; the signal just goes silent).
    /// </summary>
    public static List<int> ActiveRecorders()
    {
        var processes = new List<int>();
        object? enumerator = null;
        IMMDeviceCollection? devices = null;
        try
        {
            enumerator = CreateEnumerator();
            if (((IMMDeviceEnumerator)enumerator).EnumAudioEndpoints(DataFlowCapture, DeviceStateActive, out IntPtr collection) != 0 || collection == IntPtr.Zero)
                return processes;
            devices = (IMMDeviceCollection)Marshal.GetObjectForIUnknown(collection);
            Marshal.Release(collection);
            devices.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (devices.Item(i, out IMMDevice device) != 0) continue;
                object? manager = null;
                IAudioSessionEnumerator? sessions = null;
                try
                {
                    var id = SessionManagerId;
                    if (device.Activate(ref id, ClsCtxAll, IntPtr.Zero, out manager) != 0) continue;
                    if (((IAudioSessionManager2)manager).GetSessionEnumerator(out sessions) != 0) continue;
                    sessions.GetCount(out int sessionCount);
                    for (int s = 0; s < sessionCount; s++)
                    {
                        if (sessions.GetSession(s, out IAudioSessionControl2 session) != 0) continue;
                        try
                        {
                            if (session.GetState(out int state) == 0 && state == AudioSessionStateActive &&
                                session.GetProcessId(out uint pid) == 0 && pid != 0 && !processes.Contains((int)pid))
                                processes.Add((int)pid);
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(session);
                        }
                    }
                }
                finally
                {
                    if (sessions != null) Marshal.ReleaseComObject(sessions);
                    if (manager != null) Marshal.ReleaseComObject(manager);
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        catch (COMException ex)
        {
            RuntimeLog.Warn("CONTROL-CENTER", $"Recording sessions not readable: {ex.Message}");
        }
        finally
        {
            if (devices != null) Marshal.ReleaseComObject(devices);
            // The enumerator is not released: Windows hands the same one to BluetoothControl too.
        }
        return processes;
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
            enumerator = CreateEnumerator();
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
            // The enumerator is not released: Windows hands the same one to BluetoothControl too.
        }
    }
}
