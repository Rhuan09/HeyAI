using System.Text.Json;
using HeyAI.Core;
using HeyAI.Core.Audit;
using HeyAI.Core.Security;
using HeyAI.Core.Tools;
using HeyAI.Server.Mcp;
using Xunit;

namespace HeyAI.Tests;

/// <summary>
/// Transport-level behaviour, with no OS involved — these run in CI.
/// </summary>
public sealed class McpTransportTests
{
    /// <summary>A tool whose only job is to take a known amount of time, cancellably.</summary>
    private sealed class DelayTool(string name, TimeSpan delay) : IHeyAITool
    {
        public string Name => name;
        public string Title => name;
        public string Description => name;
        public JsonElement InputSchema => JsonDocument.Parse("""{"type":"object"}""").RootElement;
        public ToolAnnotations Annotations => ToolAnnotations.ReadOnly;
        public RiskLevel EvaluateRisk(JsonElement args) => RiskLevel.Read;

        public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct)
        {
            await Task.Delay(delay, ct);
            return ToolResult.Ok($"{name} done");
        }
    }

    private static McpServer BuildServer()
    {
        var registry = new ToolRegistry(
        [
            new DelayTool("slow_tool", TimeSpan.FromMilliseconds(400)),
            new DelayTool("fast_tool", TimeSpan.Zero),
        ]);

        var config = new HeyAIConfig { EnabledTools = ["slow_tool", "fast_tool"] };
        var taint = new TaintTracker();
        var invoker = new ToolInvoker(
            registry, new PolicyEngine(config, taint), new InMemoryAuditLog(), taint);

        return new McpServer(registry, invoker, TextWriter.Null);
    }

    // Built through the serializer rather than a raw string: the literal ends in several
    // consecutive closing braces, which collide with raw-string interpolation delimiters.
    private static string Call(int id, string tool) =>
        JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method = "tools/call",
            @params = new { name = tool, arguments = new { } },
        });

    private static async Task<List<JsonElement>> RunAsync(params string[] requests)
    {
        var output = new StringWriter();
        await BuildServer().RunAsync(
            new StringReader(string.Join('\n', requests)), output, TestContext.Current.CancellationToken);

        return output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .ToList();
    }

    [Fact]
    public async Task A_slow_call_does_not_delay_one_issued_after_it()
    {
        // The regression this guards: the read loop used to await each handler, so a
        // capture-plus-OCR would stall the transport long enough for a client keepalive
        // to time out, and the whole session would look flaky.
        var responses = await RunAsync(Call(1, "slow_tool"), Call(2, "fast_tool"));

        Assert.Equal(2, responses.Count);
        Assert.Equal(2, responses[0].GetProperty("id").GetInt32());
        Assert.Equal(1, responses[1].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Ping_is_answered_while_a_tool_is_still_running()
    {
        var responses = await RunAsync(
            Call(1, "slow_tool"),
            """{"jsonrpc":"2.0","id":2,"method":"ping"}""");

        Assert.Equal(2, responses[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Every_request_is_answered_exactly_once()
    {
        var responses = await RunAsync(
            Call(1, "slow_tool"), Call(2, "fast_tool"), Call(3, "slow_tool"), Call(4, "fast_tool"));

        var ids = responses.Select(r => r.GetProperty("id").GetInt32()).OrderBy(i => i);

        Assert.Equal([1, 2, 3, 4], ids);
    }

    [Fact]
    public async Task A_cancellation_notification_stops_the_request_it_names()
    {
        var responses = await RunAsync(
            Call(1, "slow_tool"),
            """{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":1}}""");

        var text = responses.Single()
            .GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();

        Assert.Contains("cancelled", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(responses.Single().GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task Cancelling_one_request_leaves_another_running()
    {
        var responses = await RunAsync(
            Call(1, "slow_tool"),
            Call(2, "slow_tool"),
            """{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":1}}""");

        Assert.Equal(2, responses.Count);

        var byId = responses.ToDictionary(r => r.GetProperty("id").GetInt32());
        Assert.True(byId[1].GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.False(byId[2].GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task A_malformed_line_does_not_kill_the_transport()
    {
        var responses = await RunAsync("{ this is not json", Call(1, "fast_tool"));

        Assert.Equal(2, responses.Count);
        Assert.True(responses[0].TryGetProperty("error", out _));
        Assert.Equal(1, responses[1].GetProperty("id").GetInt32());
    }
}
