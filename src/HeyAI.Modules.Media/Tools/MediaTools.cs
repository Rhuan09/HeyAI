using System.Text.Json;
using HeyAI.Core.Tools;
using HeyAI.Modules.Media.Gsmtc;

namespace HeyAI.Modules.Media.Tools;

public sealed class MediaGetStatusTool(MediaSessionService sessions) : HeyAITool
{
    public override string Name => "media_get_status";
    public override string Title => "Read media playback status";

    public override string Description =>
        "Lists active media sessions on this PC (Spotify, browser tabs, VLC, ...) with the " +
        "current track title, artist and play state. Read-only.";

    public override ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

    protected override string SchemaJson =>
        """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    public override async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        try
        {
            var list = await sessions.GetSessionsAsync(ct).ConfigureAwait(false);
            if (list.Count == 0)
            {
                return ToolResult.Ok("""{"sessions":[],"note":"No app is currently registering a media session."}""");
            }

            // Track titles and app ids are chosen by whatever is playing, so a crafted
            // page can put instructions in them. Marked tainted so the policy engine
            // refuses Critical follow-ups. See docs/SECURITY.md.
            return ToolResult.UntrustedJson(new { sessions = list }, MediaSessionService.TaintSource);
        }
        catch (Exception ex)
        {
            return ToolResult.Error("gsmtc_unavailable",
                $"Could not read media sessions: {ex.Message}");
        }
    }
}

public sealed class MediaControlTool(MediaSessionService sessions) : HeyAITool
{
    private static readonly string[] Actions =
        ["play", "pause", "toggle", "next", "previous", "stop"];

    public override string Name => "media_control";
    public override string Title => "Control media playback";

    public override string Description =>
        "Plays, pauses, skips or stops media playback. Targets the system's current media " +
        "session unless appId names another one. Reversible.";

    public override ToolAnnotations Annotations => new() { IdempotentHint = false };

    protected override string SchemaJson =>
        """
        {
          "type": "object",
          "properties": {
            "action": {
              "type": "string",
              "enum": ["play", "pause", "toggle", "next", "previous", "stop"],
              "description": "Transport command to issue."
            },
            "appId": {
              "type": "string",
              "description": "Optional substring of the app user model id, e.g. 'Spotify'. Defaults to the current session."
            }
          },
          "required": ["action"],
          "additionalProperties": false
        }
        """;

    /// <summary>
    /// Every transport command is one keystroke away for the user, so all of them are
    /// Convenience. This override exists to make the classification an explicit decision
    /// rather than an inherited default.
    /// </summary>
    public override RiskLevel EvaluateRisk(JsonElement args) => RiskLevel.Convenience;

    public override async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var action = GetString(args, "action");
        if (action is null || !Actions.Contains(action, StringComparer.Ordinal))
        {
            return ToolResult.Error("invalid_argument",
                $"'action' must be one of: {string.Join(", ", Actions)}.");
        }

        try
        {
            var session = await sessions.ResolveSessionAsync(GetString(args, "appId"), ct)
                .ConfigureAwait(false);

            if (session is null)
            {
                return ToolResult.Error("no_session",
                    "No matching media session is active right now.");
            }

            var ok = action switch
            {
                "play" => await session.TryPlayAsync().AsTask(ct).ConfigureAwait(false),
                "pause" => await session.TryPauseAsync().AsTask(ct).ConfigureAwait(false),
                "toggle" => await session.TryTogglePlayPauseAsync().AsTask(ct).ConfigureAwait(false),
                "next" => await session.TrySkipNextAsync().AsTask(ct).ConfigureAwait(false),
                "previous" => await session.TrySkipPreviousAsync().AsTask(ct).ConfigureAwait(false),
                "stop" => await session.TryStopAsync().AsTask(ct).ConfigureAwait(false),
                _ => false,
            };

            if (!ok)
            {
                // GSMTC returns false when the source does not advertise that control.
                return ToolResult.Error("command_rejected",
                    $"'{session.SourceAppUserModelId}' did not accept '{action}'. " +
                    "The app may not support that control.");
            }

            return ToolResult.Json(new { ok = true, action, appId = session.SourceAppUserModelId });
        }
        catch (Exception ex)
        {
            return ToolResult.Error("gsmtc_failed", $"Media control failed: {ex.Message}");
        }
    }
}
