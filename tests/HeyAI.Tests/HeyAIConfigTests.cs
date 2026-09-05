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
