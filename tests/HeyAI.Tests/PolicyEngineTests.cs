using System.Text.Json;
using HeyAI.Core.Security;
using HeyAI.Core.Tools;
using Xunit;

namespace HeyAI.Tests;

// These are pure-logic tests: no desktop, no audio device, no media session. They run in
// CI. Anything that touches a real WinRT/COM surface belongs in DesktopTests and is
// skipped on the build agents. See docs/ARCHITECTURE.md, "Testing".

public sealed class PolicyEngineTests
{
    private static readonly JsonElement NoArgs = JsonDocument.Parse("{}").RootElement;

    private sealed class FakeTool(string name, RiskLevel risk) : IHeyAITool
    {
        public string Name => name;
        public string Title => name;
        public string Description => name;
        public JsonElement InputSchema => NoArgs;
        public ToolAnnotations Annotations => new();
        public RiskLevel EvaluateRisk(JsonElement args) => risk;
        public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct) =>
            Task.FromResult(ToolResult.Ok("ok"));
    }

    [Fact]
    public void Denies_tool_that_is_not_in_the_allowlist()
    {
        var config = new HeyAIConfig { EnabledTools = ["media_get_status"] };
        var engine = new PolicyEngine(config, new TaintTracker());
        var tool = new FakeTool("shell_open_app", RiskLevel.Read);

        var decision = engine.Evaluate(tool, NoArgs, RiskLevel.Read);

        Assert.Equal(PolicyOutcome.Deny, decision.Outcome);
    }

    [Fact]
    public void Allows_enabled_tool_within_the_risk_ceiling()
    {
        var config = new HeyAIConfig
        {
            EnabledTools = ["media_control"],
            MaxAutoApprovedRisk = RiskLevel.Convenience,
        };
        var engine = new PolicyEngine(config, new TaintTracker());

        var decision = engine.Evaluate(
            new FakeTool("media_control", RiskLevel.Convenience), NoArgs, RiskLevel.Convenience);

        Assert.Equal(PolicyOutcome.Allow, decision.Outcome);
    }

    [Fact]
    public void Requires_confirmation_above_the_risk_ceiling()
    {
        var config = new HeyAIConfig
        {
            EnabledTools = ["shell_open_app"],
            MaxAutoApprovedRisk = RiskLevel.Convenience,
        };
        var engine = new PolicyEngine(config, new TaintTracker());

        var decision = engine.Evaluate(
            new FakeTool("shell_open_app", RiskLevel.Critical), NoArgs, RiskLevel.Critical);

        Assert.Equal(PolicyOutcome.RequireConfirmation, decision.Outcome);
    }

    [Fact]
    public void Blocks_critical_action_after_an_untrusted_read()
    {
        var config = new HeyAIConfig
        {
            EnabledTools = ["shell_open_app"],
            MaxAutoApprovedRisk = RiskLevel.Critical,
            UntrustedReadCooldownSeconds = 300,
        };
        var taint = new TaintTracker();
        taint.RecordUntrustedRead("ocr_read_text");
        var engine = new PolicyEngine(config, taint);

        var decision = engine.Evaluate(
            new FakeTool("shell_open_app", RiskLevel.Critical), NoArgs, RiskLevel.Critical);

        Assert.Equal(PolicyOutcome.Deny, decision.Outcome);
        Assert.Contains("ocr_read_text", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Untrusted_read_does_not_block_convenience_actions()
    {
        var config = new HeyAIConfig
        {
            EnabledTools = ["media_control"],
            MaxAutoApprovedRisk = RiskLevel.Convenience,
        };
        var taint = new TaintTracker();
        taint.RecordUntrustedRead("ocr_read_text");
        var engine = new PolicyEngine(config, taint);

        var decision = engine.Evaluate(
            new FakeTool("media_control", RiskLevel.Convenience), NoArgs, RiskLevel.Convenience);

        Assert.Equal(PolicyOutcome.Allow, decision.Outcome);
    }

    [Fact]
    public void Taint_expires_after_the_cooldown_window()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var taint = new TaintTracker(clock);
        taint.RecordUntrustedRead("ocr_read_text");

        clock.Advance(TimeSpan.FromSeconds(301));

        Assert.False(taint.IsTainted(TimeSpan.FromSeconds(300), out _, out _));
    }

    [Fact]
    public void Wildcard_entries_enable_a_whole_module()
    {
        var config = new HeyAIConfig { EnabledTools = ["media_*"] };

        Assert.True(config.IsEnabled("media_control"));
        Assert.True(config.IsEnabled("media_get_status"));
        Assert.False(config.IsEnabled("shell_open_app"));
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
