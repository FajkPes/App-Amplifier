using System.Runtime.InteropServices;

namespace ZesilovacAplikaci;

// ---------------------------------------------------------------------------
// Minimalni WASAPI / Core Audio interop. Jen to, co potrebujeme pro praci
// se session ve smesovaci hlasitosti: enumerace, hlasitost, mute, peak metr.
// ---------------------------------------------------------------------------

internal static class ComGuids
{
    public static readonly Guid MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    public static Guid IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    public static Guid IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
}

internal enum EDataFlow { Render = 0, Capture = 1, All = 2 }

internal enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

internal enum AudioSessionState { Inactive = 0, Active = 1, Expired = 2 }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int Item(int index, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}

[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetAt(int index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
    [PreserveSig] int Commit();
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public int PropertyId;
    public PropertyKey(Guid formatId, int propertyId) { FormatId = formatId; PropertyId = propertyId; }
}

[StructLayout(LayoutKind.Explicit)]
internal struct PropVariant
{
    [FieldOffset(0)] public short vt;
    [FieldOffset(8)] public IntPtr pointerValue;

    public string AsString() => vt == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(pointerValue) : null;

    public void Clear()
    {
        PropVariantClear(ref this);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // --- IAudioSessionManager ---
    [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int streamFlags, out IAudioSessionControl session);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int streamFlags, out ISimpleAudioVolume volume);
    // --- IAudioSessionManager2 ---
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    [PreserveSig] int RegisterSessionNotification(IAudioSessionNotification notification);
    [PreserveSig] int UnregisterSessionNotification(IAudioSessionNotification notification);
    [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr duckNotification);
    [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
}

[ComImport, Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionNotification
{
    [PreserveSig] int OnSessionCreated(IAudioSessionControl newSession);
}

[ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    [PreserveSig] int GetState(out AudioSessionState state);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eventContext);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr eventContext);
    [PreserveSig] int GetGroupingParam(out Guid groupingParam);
    [PreserveSig] int SetGroupingParam(ref Guid groupingParam, IntPtr eventContext);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr newNotifications);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr newNotifications);
}

[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // --- IAudioSessionControl ---
    [PreserveSig] int GetState(out AudioSessionState state);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eventContext);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr eventContext);
    [PreserveSig] int GetGroupingParam(out Guid groupingParam);
    [PreserveSig] int SetGroupingParam(ref Guid groupingParam, IntPtr eventContext);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr newNotifications);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr newNotifications);
    // --- IAudioSessionControl2 ---
    [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetProcessId(out int pid);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference(bool optOut);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float level, ref Guid eventContext);
    [PreserveSig] int GetMasterVolume(out float level);
    [PreserveSig] int SetMute(bool mute, ref Guid eventContext);
    [PreserveSig] int GetMute(out bool mute);
}

[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig] int GetPeakValue(out float peak);
    [PreserveSig] int GetMeteringChannelCount(out int count);
    [PreserveSig] int GetChannelsPeakValues(int count, [Out] float[] peaks);
    [PreserveSig] int QueryHardwareSupport(out int response);
}

internal static class NativeMethods
{
    public const int CLSCTX_ALL = 23;
    public const int DEVICE_STATE_ACTIVE = 0x1;
    public const int STGM_READ = 0;

    public const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr process, int flags,
        System.Text.StringBuilder exeName, ref int size);

    /// <summary>Cesta k .exe procesu; funguje i pro procesy, kde selze Process.MainModule.</summary>
    public static string GetProcessPath(int pid)
    {
        if (pid <= 0) return null;
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }
}
