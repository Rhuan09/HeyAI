using HeyAI.Core.Security;
using HeyAI.Core.Tools;
using Xunit;

namespace HeyAI.Tests;

public sealed class HeyAIConfigTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"heyai-{Guid.NewGuid():N}.json");

    [Fact]
    public void Round_trips_through_disk()
    {
        // heyai enable writes this file, and a config that fails to reload is a config
        // that silently drops the user's permission changes.
        var original = new HeyAIConfig
        {
            EnabledTools = ["media_control", "window_list_open"],
            MaxAutoApprovedRisk = RiskLevel.Read,
            BlockCriticalAfterUntrustedRead = false,
            UntrustedReadCooldownSeconds = 42,
        };

        original.Save(_path);
        var reloaded = HeyAIConfig.Load(_path);

        Assert.Equal(original.EnabledTools, reloaded.EnabledTools);
        Assert.Equal(RiskLevel.Read, reloaded.MaxAutoApprovedRisk);
        Assert.False(reloaded.BlockCriticalAfterUntrustedRead);
        Assert.Equal(42, reloaded.UntrustedReadCooldownSeconds);
    }

    [Fact]
    public void Load_creates_the_default_config_when_none_exists()
    {
        var config = HeyAIConfig.Load(_path);

        Assert.True(File.Exists(_path));
        Assert.Contains("media_get_status", config.EnabledTools);

        // The security posture must survive a fresh install: nothing above Convenience
        // runs unattended, and untrusted reads block Critical actions.
        Assert.Equal(RiskLevel.Convenience, config.MaxAutoApprovedRisk);
        Assert.True(config.BlockCriticalAfterUntrustedRead);
    }

    [Fact]
    public void Enabling_a_tool_survives_a_reload()
    {
        var config = HeyAIConfig.Load(_path);
        Assert.False(config.IsEnabled("some_future_tool"));

        config.EnabledTools.Add("some_future_tool");
        config.Save(_path);

        Assert.True(HeyAIConfig.Load(_path).IsEnabled("some_future_tool"));
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}

/// <summary>
/// The allowlist toggle, shared by `heyai enable` and the tray menu. Three outcomes rather
/// than a bool, because "nothing changed" has two meanings and collapsing them would let a
/// UI report success while the tool stays on.
/// </summary>
public sealed class ToolToggleTests
{
    [Fact]
    public void Enabling_a_disabled_tool_changes_it()
    {
        var config = new HeyAIConfig { EnabledTools = [] };

        Assert.Equal(HeyAIConfig.ToggleResult.Changed, config.SetEnabled("media_control", true));
        Assert.True(config.IsEnabled("media_control"));
    }

    [Fact]
    public void Disabling_an_enabled_tool_changes_it()
    {
        var config = new HeyAIConfig { EnabledTools = ["media_control"] };

        Assert.Equal(HeyAIConfig.ToggleResult.Changed, config.SetEnabled("media_control", false));
        Assert.False(config.IsEnabled("media_control"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Asking_for_the_state_it_is_already_in_reports_so(bool enabled)
    {
        var config = new HeyAIConfig { EnabledTools = enabled ? ["media_control"] : [] };

        Assert.Equal(
            HeyAIConfig.ToggleResult.AlreadyInThatState,
            config.SetEnabled("media_control", enabled));
    }

    [Fact]
    public void Disabling_a_tool_granted_by_a_wildcard_reports_that_rather_than_lying()
    {
        var config = new HeyAIConfig { EnabledTools = ["media_*"] };

        var result = config.SetEnabled("media_control", false);

        Assert.Equal(HeyAIConfig.ToggleResult.BlockedByWildcard, result);
        Assert.True(config.IsEnabled("media_control"));
    }

    [Fact]
    public void Disabling_removes_only_the_exact_entry()
    {
        var config = new HeyAIConfig { EnabledTools = ["media_control", "media_get_status"] };

        config.SetEnabled("media_control", false);

        Assert.Equal(["media_get_status"], config.EnabledTools);
    }
}
