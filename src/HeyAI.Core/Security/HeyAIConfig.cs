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

    /// <summary>
    /// Outcome of asking for a tool to be turned on or off.
    ///
    /// Three cases rather than a bool, because "nothing happened" has two very different
    /// meanings and collapsing them lets the UI claim success while the tool stays on.
    /// </summary>
    public enum ToggleResult
    {
        Changed,
        AlreadyInThatState,

        /// <summary>
        /// A wildcard entry grants the tool, so removing its exact name achieves nothing.
        /// Only a human editing that entry can resolve it.
        /// </summary>
        BlockedByWildcard,
    }

    /// <summary>
    /// Adds or removes a tool from the allowlist. Does not save; the caller decides when
    /// to persist, and only the CLI and the tray ever do — never a tool.
    /// </summary>
    public ToggleResult SetEnabled(string toolName, bool enabled)
    {
        if (IsEnabled(toolName) == enabled)
        {
            return ToggleResult.AlreadyInThatState;
        }

        if (enabled)
        {
            EnabledTools.Add(toolName);
            return ToggleResult.Changed;
        }

        EnabledTools.RemoveAll(e => string.Equals(e, toolName, StringComparison.Ordinal));

        return IsEnabled(toolName) ? ToggleResult.BlockedByWildcard : ToggleResult.Changed;
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
