using System.Text.Json;
using HeyAI.Core;
using HeyAI.Core.Tools;
using HeyAI.Modules.Shell;
using HeyAI.Modules.Shell.Tools;
using Xunit;

namespace HeyAI.Tests;

/// <summary>
/// The Shell module is the one that can start a program, so its guards are tested harder
/// than anything else here — and every one of these runs in CI, because refusing a path
/// needs no desktop. Nothing in this file launches anything.
/// </summary>
public sealed class ShellTests
{
    private readonly ShellOpenPathTool _tool = new();

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement;

    private Task<ToolResult> Execute(string json) =>
        _tool.ExecuteAsync(Args(json), TestContext.Current.CancellationToken);

    [Fact]
    public void Opening_anything_is_critical()
    {
        // Deliberately not classified by file extension. A path with no extension may be a
        // file, an extension need not match the registered handler, and the target can
        // change between this call and execution -- so anything learned from the string or
        // the disk would be a time-of-check answer to a time-of-use question.
        Assert.Equal(RiskLevel.Critical, _tool.EvaluateRisk(Args("""{"path":"C:\\Users"}""")));
        Assert.Equal(RiskLevel.Critical, _tool.EvaluateRisk(Args("""{"path":"C:\\a\\b.txt"}""")));
        Assert.Equal(RiskLevel.Critical, _tool.EvaluateRisk(Args("""{"path":"C:\\a\\b.exe"}""")));
    }

    [Fact]
    public void Revealing_is_only_convenience()
    {
        // Reveal always does exactly one thing: Explorer, with the item selected. It never
        // launches a registered program, which is what makes it cheaper.
        Assert.Equal(
            RiskLevel.Convenience,
            _tool.EvaluateRisk(Args("""{"path":"C:\\a\\b.exe","mode":"reveal"}""")));
    }

    [Fact]
    public async Task Refuses_a_url()
    {
        // Reaching the network is a different tool with a different threat model, not a
        // convenience to fold into this one.
        var result = await Execute("""{"path":"https://example.com"}""");

        Assert.True(result.IsError);
        Assert.Equal("path_refused", result.ErrorCode);
        Assert.Contains("URL", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_heyais_own_state_directory()
    {
        var result = await Execute(
            JsonSerializer.Serialize(new { path = HeyAIPaths.AuditLogFile }));

        Assert.True(result.IsError);
        Assert.Contains("audit log", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Traversal_cannot_reach_the_state_directory_sideways()
    {
        // The guard resolves before it compares, so ..\..\ walking back into the state
        // folder is caught rather than slipping past a prefix check on the raw string.
        var sneaky = Path.Combine(HeyAIPaths.Root, "..", "HeyAI", "logs", "audit.jsonl");

        var verdict = ShellPathGuard.Check(sneaky);

        Assert.False(verdict.Allowed);
    }

    [Fact]
    public async Task Refuses_a_path_that_does_not_exist()
    {
        // Said plainly rather than launched hopefully: ShellExecute on a missing path can
        // still pop an "how do you want to open this" dialog at the user.
        var result = await Execute(
            JsonSerializer.Serialize(new { path = $@"C:\{Guid.NewGuid():N}\nope.txt" }));

        Assert.True(result.IsError);
        Assert.Contains("Nothing exists", result.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_an_empty_path(string path)
    {
        Assert.False(ShellPathGuard.Check(path).Allowed);
    }

    [Fact]
    public async Task Rejects_an_unknown_mode()
    {
        var result = await Execute("""{"path":"C:\\Windows","mode":"execute"}""");

        Assert.True(result.IsError);
        Assert.Equal("invalid_argument", result.ErrorCode);
    }

    [Fact]
    public void Allows_an_ordinary_existing_folder()
    {
        var verdict = ShellPathGuard.Check(Path.GetTempPath());

        Assert.True(verdict.Allowed);
        Assert.NotNull(verdict.FullPath);
    }

    [Fact]
    public void The_schema_refuses_unknown_fields()
    {
        // An unvalidated field is a way past EvaluateRisk, and this is the tool where that
        // would matter most.
        Assert.False(_tool.InputSchema.GetProperty("additionalProperties").GetBoolean());
    }
}
