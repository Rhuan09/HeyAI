using System.Text.Json;
using HeyAI.Core.Tools;
using HeyAI.Modules.Media.Audio;

namespace HeyAI.Modules.Media.Tools;

public sealed class AudioGetDevicesTool(AudioService audio) : HeyAITool
{
    public override string Name => "audio_get_devices";
    public override string Title => "List audio output devices";

    public override string Description =>
        "Lists active audio output devices with their master volume and mute state, plus " +
        "the per-application mixer sessions. Read-only.";

    public override ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

    protected override string SchemaJson =>
        """
        {
          "type": "object",
          "properties": {
            "includeSessions": {
              "type": "boolean",
              "description": "Also list per-application mixer sessions. Defaults to true."
            }
          },
          "additionalProperties": false
        }
        """;

    public override Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var includeSessions = GetBool(args, "includeSessions") ?? true;

        try
        {
            var devices = audio.GetRenderDevices();
            var sessions = includeSessions ? audio.GetSessions() : null;

            // Device friendly names and session display names come from drivers and
            // third-party apps, so they are outside our trust boundary.
            return Task.FromResult(ToolResult.UntrustedJson(
                new { devices, sessions }, "core-audio-device-names"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error("audio_unavailable",
                $"Could not read audio devices: {ex.Message}"));
        }
    }
}

public sealed class AudioSetVolumeTool(AudioService audio) : HeyAITool
{
    public override string Name => "audio_set_volume";
    public override string Title => "Set audio volume";

    public override string Description =>
        "Sets the master output volume, or the volume of a single application in the " +
        "Windows mixer. Level is 0.0 to 1.0. Can also mute or unmute. Reversible.";

    public override ToolAnnotations Annotations => new() { IdempotentHint = true };

    protected override string SchemaJson =>
        """
        {
          "type": "object",
          "properties": {
            "target": {
              "type": "string",
              "enum": ["master", "app"],
              "description": "'master' for system volume, 'app' to target one application."
            },
            "app": {
              "type": "string",
              "description": "Process or display name substring, e.g. 'spotify'. Required when target is 'app'."
            },
            "level": {
              "type": "number",
              "minimum": 0,
              "maximum": 1,
              "description": "New volume as a fraction, 0.0 to 1.0."
            },
            "mute": {
              "type": "boolean",
              "description": "Mute or unmute. May be combined with level."
            }
          },
          "required": ["target"],
          "additionalProperties": false
        }
        """;

    public override RiskLevel EvaluateRisk(JsonElement args) => RiskLevel.Convenience;

    public override Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
    {
        var target = GetString(args, "target");
        var level = GetNumber(args, "level") is { } l ? (float)l : (float?)null;
        var mute = GetBool(args, "mute");

        if (level is null && mute is null)
        {
            return Task.FromResult(ToolResult.Error("invalid_argument",
                "Provide at least one of 'level' or 'mute'."));
        }

        if (level is not null && (level < 0f || level > 1f))
        {
            return Task.FromResult(ToolResult.Error("invalid_argument",
                "'level' must be between 0.0 and 1.0."));
        }

        try
        {
            switch (target)
            {
                case "master":
                {
                    var (finalLevel, finalMute) = audio.SetMasterVolume(level, mute);
                    return Task.FromResult(ToolResult.Json(
                        new { ok = true, target, level = finalLevel, muted = finalMute }));
                }

                case "app":
                {
                    var app = GetString(args, "app");
                    if (string.IsNullOrWhiteSpace(app))
                    {
                        return Task.FromResult(ToolResult.Error("invalid_argument",
                            "'app' is required when target is 'app'."));
                    }

                    var touched = audio.SetSessionVolume(app, level, mute);
                    if (touched.Count == 0)
                    {
                        return Task.FromResult(ToolResult.Error("no_session",
                            $"No audio session matched '{app}'. Call audio_get_devices to see what is playing."));
                    }

                    return Task.FromResult(ToolResult.Json(new { ok = true, target, sessions = touched }));
                }

                default:
                    return Task.FromResult(ToolResult.Error("invalid_argument",
                        "'target' must be 'master' or 'app'."));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error("audio_failed", $"Volume change failed: {ex.Message}"));
        }
    }
}
