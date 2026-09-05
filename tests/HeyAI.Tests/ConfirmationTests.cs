using System.Text;
using System.Text.Json;
using HeyAI.Core;
using HeyAI.Core.Audit;
using HeyAI.Core.Confirmation;
using HeyAI.Core.Security;
using HeyAI.Core.Tools;
using Xunit;

namespace HeyAI.Tests;

/// <summary>
/// The confirmation path. Named pipes need no display, so even the wire test runs in CI.
/// </summary>
public sealed class ConfirmationTests
{
    private static readonly JsonElement NoArgs = JsonDocument.Parse("{}").RootElement;

    private sealed class RecordingTool : IHeyAITool
    {
        public bool WasExecuted { get; private set; }

        public string Name => "fake_launch";
        public string Title => "Launch something";
        public string Description => "test";
        public JsonElement InputSchema => NoArgs;
        public ToolAnnotations Annotations => new() { DestructiveHint = true };
        public RiskLevel EvaluateRisk(JsonElement args) => RiskLevel.Critical;

        public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
        {
            WasExecuted = true;
            return Task.FromResult(ToolResult.Ok("launched"));
        }
    }

    private sealed class ScriptedPrompt(bool approve) : IConfirmationPrompt
    {
        public ConfirmationRequest? Seen { get; private set; }

        public Task<ConfirmationResponse> AskAsync(ConfirmationRequest request, CancellationToken ct)
        {
            Seen = request;
            return Task.FromResult(approve
                ? ConfirmationResponse.Approved_("approved in test")
                : ConfirmationResponse.Denied("refused in test"));
        }
    }

    private static (ToolInvoker Invoker, RecordingTool Tool, InMemoryAuditLog Audit) Build(
        IConfirmationPrompt prompt)
    {
        var tool = new RecordingTool();
        var registry = new ToolRegistry([tool]);

        var config = new HeyAIConfig
        {
            EnabledTools = ["fake_launch"],

            // Critical is above the ceiling, which is what routes it to a human.
            MaxAutoApprovedRisk = RiskLevel.Convenience,
            BlockCriticalAfterUntrustedRead = false,
        };

        var taint = new TaintTracker();
        var audit = new InMemoryAuditLog();

        return (new ToolInvoker(registry, new PolicyEngine(config, taint), audit, taint, prompt),
                tool, audit);
    }

    private static Task<ToolResult> Call(ToolInvoker invoker) =>
        invoker.InvokeAsync("fake_launch", NoArgs, "test-client", TestContext.Current.CancellationToken);

    [Fact]
    public async Task An_approved_action_actually_runs()
    {
        // Before this, RequireConfirmation was a dead branch that always refused. This is
        // the test that says Critical now means something other than Deny.
        var (invoker, tool, _) = Build(new ScriptedPrompt(approve: true));

        var result = await Call(invoker);

        Assert.False(result.IsError);
        Assert.True(tool.WasExecuted);
    }

    [Fact]
    public async Task A_refused_action_does_not_run()
    {
        var (invoker, tool, _) = Build(new ScriptedPrompt(approve: false));

        var result = await Call(invoker);

        Assert.True(result.IsError);
        Assert.Equal("confirmation_denied", result.ErrorCode);
        Assert.False(tool.WasExecuted);
    }

    [Fact]
    public async Task The_prompt_is_told_what_it_is_approving()
    {
        var prompt = new ScriptedPrompt(approve: false);
        var (invoker, _, _) = Build(prompt);

        await Call(invoker);

        Assert.NotNull(prompt.Seen);
        Assert.Equal("fake_launch", prompt.Seen.ToolName);
        Assert.Equal(RiskLevel.Critical, prompt.Seen.Risk);
        Assert.Equal("test-client", prompt.Seen.Client);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_human_decision_is_audited(bool approved)
    {
        var (invoker, _, audit) = Build(new ScriptedPrompt(approved));

        await Call(invoker);

        // "A human said yes" and "the rules said yes" are different claims, so the log has
        // to keep them apart.
        var entry = audit.Entries.First(e => e.Outcome == PolicyOutcome.RequireConfirmation);
        Assert.Equal(approved, entry.ConfirmedByHuman);
    }

    [Fact]
    public async Task An_unwired_host_refuses_rather_than_allowing()
    {
        var (invoker, tool, _) = Build(new DenyingConfirmationPrompt());

        Assert.True((await Call(invoker)).IsError);
        Assert.False(tool.WasExecuted);
    }

    [Fact]
    public async Task With_no_tray_listening_the_pipe_prompt_denies()
    {
        // The most important failure mode: if a broken or absent tray meant "allowed",
        // killing the tray would be the easiest privilege escalation on the machine.
        var prompt = new NamedPipeConfirmationPrompt(
            timeout: TimeSpan.FromSeconds(5),
            pipeName: $"HeyAI.Test.Absent.{Guid.NewGuid():N}");

        var response = await prompt.AskAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(response.Approved);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_real_pipe_round_trip_carries_the_answer(bool approve)
    {
        var name = $"HeyAI.Test.{Guid.NewGuid():N}";
        var ct = TestContext.Current.CancellationToken;

        var listener = Task.Run(async () =>
        {
            await using var pipe = ConfirmationPipe.CreateServer(name, 1);
            await pipe.WaitForConnectionAsync(ct);

            using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true,
            };

            var line = await reader.ReadLineAsync(ct);
            var received = JsonSerializer.Deserialize<ConfirmationRequest>(line!, ConfirmationPipe.Json);

            await writer.WriteLineAsync(JsonSerializer.Serialize(
                approve
                    ? ConfirmationResponse.Approved_("yes")
                    : ConfirmationResponse.Denied("no"),
                ConfirmationPipe.Json));

            return received;
        }, ct);

        var prompt = new NamedPipeConfirmationPrompt(TimeSpan.FromSeconds(10), name);
        var response = await prompt.AskAsync(Request(), ct);
        var seen = await listener;

        Assert.Equal(approve, response.Approved);

        // The dialog renders these, so a field lost in transit would mean a person
        // approving something other than what they were shown.
        Assert.Equal("fake_launch", seen!.ToolName);
        Assert.Equal(RiskLevel.Critical, seen.Risk);
        Assert.Contains("evil", seen.ArgumentsJson, StringComparison.Ordinal);
    }

    private static ConfirmationRequest Request() => new()
    {
        ToolName = "fake_launch",
        ToolTitle = "Launch something",
        Risk = RiskLevel.Critical,
        Reason = "above the configured ceiling",
        ArgumentsJson = """{"path":"evil.exe"}""",
        Client = "test-client",
    };
}
