using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace HeyAI.Modules.Media.Audio;

// Core Audio is classic COM, not WinRT: GSMTC has no volume concept at all, so per-app
// and master volume come from MMDevice/AudioSession. Hand-rolled with source-generated
// COM ([GeneratedComInterface]) rather than taking a NAudio dependency, because the
// whole surface we need is four interfaces and a package would be the project's only
// non-SDK runtime dependency.
//
// Every method is [PreserveSig] returning HRESULT. Implicit HRESULT-to-exception
// translation hides the "no active session" and "device disconnected" cases that are
// normal here, and those must become structured tool errors rather than throws.
//
// Threading: MTA-safe. Do NOT route these through IWinRtDispatcher.

internal enum EDataFlow
{
    Render = 0,
    Capture = 1,
    All = 2,
}

internal enum ERole
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

internal enum AudioSessionState
{
    Inactive = 0,
    Active = 1,
    Expired = 2,
}

[GeneratedComInterface]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal partial interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    [PreserveSig] int GetDevice(IntPtr id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}

[GeneratedComInterface]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal partial interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}

[GeneratedComInterface]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal partial interface IMMDevice
{
    [PreserveSig] int Activate(in Guid iid, uint clsCtx, IntPtr activationParams, out IntPtr instance);
    [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
    [PreserveSig] int GetId(out IntPtr id);
    [PreserveSig] int GetState(out uint state);
}

[GeneratedComInterface]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal partial interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
    [PreserveSig] int SetValue(in PropertyKey key, in PropVariant value);
    [PreserveSig] int Commit();
}

[GeneratedComInterface]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal partial interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float levelDb, IntPtr eventContext);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);
    [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, IntPtr eventContext);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
    [PreserveSig] int VolumeStepUp(IntPtr eventContext);
    [PreserveSig] int VolumeStepDown(IntPtr eventContext);
    [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
    [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}

[GeneratedComInterface]
[Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4")]
internal partial interface IAudioSessionManager
{
    [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint streamFlags, out IntPtr sessionControl);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint streamFlags, out ISimpleAudioVolume volume);
}

[GeneratedComInterface]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
internal partial interface IAudioSessionManager2 : IAudioSessionManager
{
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
    [PreserveSig] int RegisterSessionNotification(IntPtr notification);
    [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
    [PreserveSig] int RegisterDuckNotification(IntPtr sessionId, IntPtr duckNotification);
    [PreserveSig] int UnregisterDuckNotification(IntPtr duckNotification);
}

[GeneratedComInterface]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
internal partial interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
}

[GeneratedComInterface]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
internal partial interface IAudioSessionControl
{
    [PreserveSig] int GetState(out AudioSessionState state);
    [PreserveSig] int GetDisplayName(out IntPtr name);
    [PreserveSig] int SetDisplayName(IntPtr value, IntPtr eventContext);
    [PreserveSig] int GetIconPath(out IntPtr path);
    [PreserveSig] int SetIconPath(IntPtr value, IntPtr eventContext);
    [PreserveSig] int GetGroupingParam(out Guid groupingParam);
    [PreserveSig] int SetGroupingParam(in Guid over, IntPtr eventContext);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr newNotifications);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr newNotifications);
}

[GeneratedComInterface]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
internal partial interface IAudioSessionControl2 : IAudioSessionControl
{
    [PreserveSig] int GetSessionIdentifier(out IntPtr id);
    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[GeneratedComInterface]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
internal partial interface ISimpleAudioVolume
{
    [PreserveSig] int SetMasterVolume(float level, IntPtr eventContext);
    [PreserveSig] int GetMasterVolume(out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
}

/// <summary>
/// Only the layout we actually read. The union starts at offset 8 on x64 (4 on x86);
/// for VT_LPWSTR that slot holds the string pointer. Always PropVariantClear it.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort Vt;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Value;
    public IntPtr Value2;

    public const ushort VT_LPWSTR = 31;
}

internal static partial class CoreAudio
{
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    internal static readonly PropertyKey PKEY_Device_FriendlyName = new()
    {
        FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        PropertyId = 14,
    };

    internal const uint CLSCTX_INPROC_SERVER = 0x1;
    internal const uint STGM_READ = 0x0;
    internal const uint DEVICE_STATE_ACTIVE = 0x1;

    private static readonly StrategyBasedComWrappers Wrappers = new();

    internal static IMMDeviceEnumerator CreateDeviceEnumerator()
    {
        var clsid = CLSID_MMDeviceEnumerator;
        var iid = typeof(IMMDeviceEnumerator).GUID;
        Marshal.ThrowExceptionForHR(
            CoCreateInstance(in clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, in iid, out var ptr));
        return Wrap<IMMDeviceEnumerator>(ptr);
    }

    /// <summary>Wraps a raw COM pointer and releases the caller's reference.</summary>
    internal static T Wrap<T>(IntPtr ptr) where T : class
    {
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(ptr);
        }
    }

    /// <summary>Reads and frees a COM-allocated wide string returned through an out param.</summary>
    internal static string? TakeString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUni(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    internal static string? ReadFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(STGM_READ, out var store) < 0) return null;

        var key = PKEY_Device_FriendlyName;
        if (store.GetValue(in key, out var variant) < 0) return null;

        try
        {
            return variant.Vt == PropVariant.VT_LPWSTR ? Marshal.PtrToStringUni(variant.Value) : null;
        }
        finally
        {
            PropVariantClear(ref variant);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid clsid, IntPtr outer, uint clsContext, in Guid iid, out IntPtr instance);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant variant);
}
