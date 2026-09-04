using Windows.Media.Control;

namespace HeyAI.Modules.Media.Gsmtc;

public sealed record MediaSessionInfo
{
    public required string AppId { get; init; }
    public required bool IsCurrent { get; init; }
    public required string PlaybackStatus { get; init; }
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? AlbumTitle { get; init; }
    public int? TrackNumber { get; init; }
    public IReadOnlyList<string>? Genres { get; init; }
}

/// <summary>
/// Wraps the Global System Media Transport Controls, the same surface the Win+volume
/// overlay uses. Works unpackaged, and works for anything that registers a session:
/// Spotify, the browser, VLC, Groove.
///
/// Threading: GSMTC is MTA-safe, so this deliberately does NOT go through
/// <c>IWinRtDispatcher</c>. Routing it there would serialise every call behind the
/// message pump for no benefit. See that interface's remarks.
/// </summary>
public sealed class MediaSessionService
{
    /// <summary>
    /// GSMTC metadata is attacker-influenceable: a track title or app id is arbitrary
    /// text chosen by whatever is playing, and a crafted page can set it. Callers must
    /// surface it through <c>ToolResult.UntrustedJson</c>.
    /// </summary>
    public const string TaintSource = "gsmtc-media-metadata";

    public async Task<IReadOnlyList<MediaSessionInfo>> GetSessionsAsync(CancellationToken ct)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager
            .RequestAsync().AsTask(ct).ConfigureAwait(false);

        var current = manager.GetCurrentSession();
        var currentId = current?.SourceAppUserModelId;

        var results = new List<MediaSessionInfo>();
        foreach (var session in manager.GetSessions())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await DescribeAsync(session, session.SourceAppUserModelId == currentId, ct)
                .ConfigureAwait(false));
        }

        return results;
    }

    private static async Task<MediaSessionInfo> DescribeAsync(
        GlobalSystemMediaTransportControlsSession session, bool isCurrent, CancellationToken ct)
    {
        var playback = session.GetPlaybackInfo();

        GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
        try
        {
            props = await session.TryGetMediaPropertiesAsync().AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Some sources register a session but refuse metadata. A session with no
            // properties is still worth reporting, so this is not an error.
        }

        return new MediaSessionInfo
        {
            AppId = session.SourceAppUserModelId,
            IsCurrent = isCurrent,
            PlaybackStatus = playback.PlaybackStatus.ToString(),
            Title = props?.Title,
            Artist = props?.Artist,
            AlbumTitle = props?.AlbumTitle,
            TrackNumber = props?.TrackNumber,
            Genres = props?.Genres?.ToArray(),
        };
    }

    /// <summary>Resolves the session to act on: an explicit app id, else the current one.</summary>
    public async Task<GlobalSystemMediaTransportControlsSession?> ResolveSessionAsync(
        string? appId, CancellationToken ct)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager
            .RequestAsync().AsTask(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(appId))
        {
            return manager.GetCurrentSession();
        }

        return manager.GetSessions().FirstOrDefault(s =>
            s.SourceAppUserModelId.Contains(appId, StringComparison.OrdinalIgnoreCase));
    }
}
