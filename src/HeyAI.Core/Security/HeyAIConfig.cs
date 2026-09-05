using System.Text.Json;
using System.Text.Json.Serialization;
using HeyAI.Core.Tools;

namespace HeyAI.Core.Security;

/// <summary>
/// Deny-by-default configuration, loaded from %LOCALAPPDATA%\HeyAI\config.json.
/// A tool that is not explicitly enabled does not run, even if it is registered.
/// </summary>
public sealed class HeyAIConfig
{
    /// <summary>Tool names the user has opted into. Supports a trailing <c>*</c> wildcard.</summary>
    public List<string> EnabledTools { get; set; } = [];

    /// <summary>Highest risk tier that runs without an interactive confirmation.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<RiskLevel>))]
    public RiskLevel MaxAutoApprovedRisk { get; set; } = RiskLevel.Convenience;

    /// <summary>
    /// Refuse Critical actions for a window of time after the agent has ingested
    /// untrusted screen content. This is the mitigation for the OCR-injection chain;
    /// turning it off is a deliberate, documented downgrade.
    /// </summary>
    public bool BlockCriticalAfterUntrustedRead { get; set; } = true;

    public int UntrustedReadCooldownSeconds { get; set; } = 300;

    public static HeyAIConfig Default() => new()
    {
        // Read-only + trivially reversible media control. Nothing that reaches the
        // network, the filesystem, or process creation is on by default.
        EnabledTools =
        [
            "media_get_status",
            "media_control",
            "audio_get_devices",
            "audio_set_volume",
            "window_list_open",
        ],
    };

    public static HeyAIConfig Load(string? path = null)
    {
        path ??= HeyAIPaths.ConfigFile;
        if (!File.Exists(path))
        {
            var fresh = Default();
            HeyAIPaths.EnsureCreated();
            File.WriteAllText(path, JsonSerializer.Serialize(fresh, WriteOptions));
            return fresh;
        }

        return JsonSerializer.Deserialize<HeyAIConfig>(File.ReadAllText(path), ReadOptions)
               ?? Default();
    }

    /// <summary>
    /// Persists the config. Deliberately not reachable from any tool: the agent must not
    /// be able to widen its own permissions, so this is only called from the CLI, where a
    /// human typed the command.
    /// </summary>
    public void Save(string? path = null)
    {
        path ??= HeyAIPaths.ConfigFile;
        HeyAIPaths.EnsureCreated();
        File.WriteAllText(path, JsonSerializer.Serialize(this, WriteOptions));
    }

    public bool IsEnabled(string toolName)
    {
        foreach (var entry in EnabledTools)
        {
            if (entry.EndsWith('*'))
            {
                if (toolName.StartsWith(entry[..^1], StringComparison.Ordinal)) return true;
            }
            else if (string.Equals(entry, toolName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
}
