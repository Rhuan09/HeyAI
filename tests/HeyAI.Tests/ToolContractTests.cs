using System.Text.Json;
using HeyAI.Core;
using HeyAI.Core.Audit;
using HeyAI.Core.Security;
using HeyAI.Core.Tools;
using HeyAI.Modules.Media;
using Xunit;

namespace HeyAI.Tests;

/// <summary>
/// Contract tests over the registered tool set. These construct the tools but never call
/// them, so they need no desktop session and run in CI.
/// </summary>
public sealed class ToolContractTests
{
    private static ToolRegistry BuildRegistry()
    {
        var registry = new ToolRegistry();
        registry.RegisterAll(MediaModule.CreateTools());
        return registry;
    }

    [Fact]
    public void Every_tool_has_a_strict_object_schema()
    {
        foreach (var tool in BuildRegistry().All)
        {
            Assert.Equal(JsonValueKind.Object, tool.InputSchema.ValueKind);
            Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());

            // additionalProperties:false is what stops a model from smuggling an
            // unvalidated field past EvaluateRisk.
            Assert.True(tool.InputSchema.TryGetProperty("additionalProperties", out var extra),
                $"{tool.Name} must declare additionalProperties.");
            Assert.False(extra.GetBoolean(), $"{tool.Name} must set additionalProperties:false.");
        }
    }

    [Fact]
    public void Read_only_tools_never_classify_above_read()
    {
        var noArgs = JsonDocument.Parse("{}").RootElement;

        foreach (var tool in BuildRegistry().All.Where(t => t.Annotations.ReadOnlyHint))
        {
            Assert.Equal(RiskLevel.Read, tool.EvaluateRisk(noArgs));
        }
    }

    [Fact]
    public void Tool_names_are_snake_case_and_module_prefixed()
    {
        foreach (var tool in BuildRegistry().All)
        {
            Assert.Matches("^[a-z]+(_[a-z]+)+$", tool.Name);
        }
    }

    [Fact]
    public async Task Unknown_tool_is_reported_not_thrown()
    {
        var invoker = BuildInvoker(HeyAIConfig.Default(), out _);

        var result = await invoker.InvokeAsync(
            "does_not_exist", JsonDocument.Parse("{}").RootElement, "test", CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("unknown_tool", result.ErrorCode);
    }

    [Fact]
    public async Task Disabled_tool_is_denied_and_audited()
    {
        var config = new HeyAIConfig { EnabledTools = [] };
        var invoker = BuildInvoker(config, out var audit);

        var result = await invoker.InvokeAsync(
            "media_control",
            JsonDocument.Parse("""{"action":"pause"}""").RootElement,
            "test",
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("denied_by_policy", result.ErrorCode);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("media_control", entry.Tool);
        Assert.Equal(PolicyOutcome.Deny, entry.Outcome);
    }

    private static ToolInvoker BuildInvoker(HeyAIConfig config, out InMemoryAuditLog audit)
    {
        audit = new InMemoryAuditLog();
        var taint = new TaintTracker();
        return new ToolInvoker(BuildRegistry(), new PolicyEngine(config, taint), audit, taint);
    }
}
