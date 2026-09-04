using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HeyAI.Modules.Media.Audio;

public sealed record AudioDeviceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsDefault { get; init; }
    public required float Volume { get; init; }
    public required bool Muted { get; init; }
}

public sealed record AudioSessionInfo
{
    public required uint ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public string? DisplayName { get; init; }
    public required string State { get; init; }
    public required float Volume { get; init; }
    public required bool Muted { get; init; }
}

public sealed class AudioException(string message) : Exception(message);

/// <summary>
/// Master and per-application volume over MMDevice / AudioSession.
///
/// Threading: MTA-safe COM, called directly off the thread pool. Not dispatched.
/// </summary>
public sealed class AudioService
{
    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        var enumerator = CoreAudio.CreateDeviceEnumerator();

        string? defaultId = null;
        if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var def) >= 0
            && def.GetId(out var defIdPtr) >= 0)
        {
            defaultId = CoreAudio.TakeString(defIdPtr);
        }

        Check(enumerator.EnumAudioEndpoints(EDataFlow.Render, CoreAudio.DEVICE_STATE_ACTIVE, out var collection),
            "Could not enumerate audio render endpoints.");
        Check(collection.GetCount(out var count), "Could not count audio endpoints.");

        var devices = new List<AudioDeviceInfo>((int)count);
        for (uint i = 0; i < count; i++)
        {
            if (collection.Item(i, out var device) < 0) continue;
            if (device.GetId(out var idPtr) < 0) continue;

            var id = CoreAudio.TakeString(idPtr);
            if (id is null) continue;

            var volume = ActivateEndpointVolume(device);
            volume.GetMasterVolumeLevelScalar(out var level);
            volume.GetMute(out var muted);

            devices.Add(new AudioDeviceInfo
            {
                Id = id,
                Name = CoreAudio.ReadFriendlyName(device) ?? "Unknown device",
                IsDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase),
                Volume = level,
                Muted = muted,
            });
        }

        return devices;
    }

    public (float Volume, bool Muted) SetMasterVolume(float? level, bool? mute)
    {
        var enumerator = CoreAudio.CreateDeviceEnumerator();
        Check(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device),
            "No default audio output device.");

        var volume = ActivateEndpointVolume(device);

        if (level is not null)
        {
            Check(volume.SetMasterVolumeLevelScalar(Math.Clamp(level.Value, 0f, 1f), IntPtr.Zero),
                "Could not set master volume.");
        }

        if (mute is not null)
        {
            Check(volume.SetMute(mute.Value, IntPtr.Zero), "Could not set master mute.");
        }

        volume.GetMasterVolumeLevelScalar(out var finalLevel);
        volume.GetMute(out var finalMute);
        return (finalLevel, finalMute);
    }

    public IReadOnlyList<AudioSessionInfo> GetSessions()
    {
        var sessions = new List<AudioSessionInfo>();

        foreach (var (control, volume) in EnumerateSessions())
        {
            control.GetProcessId(out var pid);
            control.GetState(out var state);
            control.GetDisplayName(out var namePtr);
            volume.GetMasterVolume(out var level);
            volume.GetMute(out var muted);

            sessions.Add(new AudioSessionInfo
            {
                ProcessId = pid,
                ProcessName = ProcessNameOf(pid),
                DisplayName = CoreAudio.TakeString(namePtr) is { Length: > 0 } n ? n : null,
                State = state.ToString(),
                Volume = level,
                Muted = muted,
            });
        }

        return sessions;
    }

    /// <summary>
    /// Applies to every session whose process name or display name matches. Matching by
    /// name rather than pid is what a model can realistically supply ("spotify"), and
    /// browsers legitimately hold several sessions at once.
    /// </summary>
    public IReadOnlyList<AudioSessionInfo> SetSessionVolume(string match, float? level, bool? mute)
    {
        var touched = new List<AudioSessionInfo>();

        foreach (var (control, volume) in EnumerateSessions())
        {
            control.GetProcessId(out var pid);
            var processName = ProcessNameOf(pid);
            control.GetDisplayName(out var namePtr);
            var displayName = CoreAudio.TakeString(namePtr);

            var matches =
                processName.Contains(match, StringComparison.OrdinalIgnoreCase) ||
                (displayName?.Contains(match, StringComparison.OrdinalIgnoreCase) ?? false);

            if (!matches) continue;

            if (level is not null)
            {
                Check(volume.SetMasterVolume(Math.Clamp(level.Value, 0f, 1f), IntPtr.Zero),
                    $"Could not set volume for '{processName}'.");
            }

            if (mute is not null)
            {
                Check(volume.SetMute(mute.Value, IntPtr.Zero), $"Could not mute '{processName}'.");
            }

            control.GetState(out var state);
            volume.GetMasterVolume(out var finalLevel);
            volume.GetMute(out var finalMute);

            touched.Add(new AudioSessionInfo
            {
                ProcessId = pid,
                ProcessName = processName,
                DisplayName = displayName is { Length: > 0 } ? displayName : null,
                State = state.ToString(),
                Volume = finalLevel,
                Muted = finalMute,
            });
        }

        return touched;
    }

    private static IEnumerable<(IAudioSessionControl2 Control, ISimpleAudioVolume Volume)> EnumerateSessions()
    {
        var enumerator = CoreAudio.CreateDeviceEnumerator();
        Check(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device),
            "No default audio output device.");

        var iid = typeof(IAudioSessionManager2).GUID;
        Check(device.Activate(in iid, CoreAudio.CLSCTX_INPROC_SERVER, IntPtr.Zero, out var managerPtr),
            "Could not activate the audio session manager.");
        var manager = CoreAudio.Wrap<IAudioSessionManager2>(managerPtr);

        Check(manager.GetSessionEnumerator(out var sessions), "Could not enumerate audio sessions.");
        Check(sessions.GetCount(out var count), "Could not count audio sessions.");

        for (var i = 0; i < count; i++)
        {
            if (sessions.GetSession(i, out var session) < 0) continue;

            // IAudioSessionControl2 and ISimpleAudioVolume both live on the session object.
            if (session is not IAudioSessionControl2 control2) continue;
            if (session is not ISimpleAudioVolume volume) continue;

            // Skip the system sounds session: an agent muting it is never what was meant.
            if (control2.IsSystemSoundsSession() == 0) continue;

            yield return (control2, volume);
        }
    }

    private static IAudioEndpointVolume ActivateEndpointVolume(IMMDevice device)
    {
        var iid = typeof(IAudioEndpointVolume).GUID;
        Check(device.Activate(in iid, CoreAudio.CLSCTX_INPROC_SERVER, IntPtr.Zero, out var ptr),
            "Could not activate endpoint volume control.");
        return CoreAudio.Wrap<IAudioEndpointVolume>(ptr);
    }

    private static string ProcessNameOf(uint pid)
    {
        if (pid == 0) return "system";
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            // The process can exit between enumeration and lookup; that is not an error.
            return $"pid:{pid}";
        }
    }

    private static void Check(int hr, string message)
    {
        if (hr < 0) throw new AudioException($"{message} (HRESULT 0x{hr:X8})");
    }
}
