using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeyAI.Core.Tools;

/// <summary>
/// Internal risk tier for a *specific invocation*. Deliberately evaluated from the
/// arguments, not declared statically on the tool: `shell_open_path` on Documents and
/// `shell_open_app` on `powershell -enc &lt;base64&gt;` cannot share a tier.
/// </summary>
public enum RiskLevel
{
    /// <summary>Observes state only. Never mutates the machine.</summary>
    Read = 0,

    /// <summary>Mutates state, but trivially reversible by the user (volume, focus, pause).</summary>
    Convenience = 1,

    /// <summary>Not trivially reversible, or can reach outside the machine. Never auto-approved.</summary>
    Critical = 2,
}

/// <summary>
/// MCP tool annotations (spec 2025-06-18). These are what *clients* reason about, so
/// <see cref="RiskLevel"/> must map onto them rather than living as a parallel invention.
/// </summary>
public sealed record ToolAnnotations
{
    [JsonPropertyName("readOnlyHint")] public bool ReadOnlyHint { get; init; }
    [JsonPropertyName("destructiveHint")] public bool DestructiveHint { get; init; }
    [JsonPropertyName("idempotentHint")] public bool IdempotentHint { get; init; }
    [JsonPropertyName("openWorldHint")] public bool OpenWorldHint { get; init; }

    public static ToolAnnotations ReadOnly => new() { ReadOnlyHint = true, IdempotentHint = true };
}

/// <summary>
/// A binary image a tool produced. Base64 because that is what MCP's image content block
/// carries; the client turns it back into pixels for the model.
/// </summary>
public sealed record ToolImage(string Base64Data, string MimeType, int Width, int Height);

/// <summary>Result of one tool invocation. Never throws across this boundary.</summary>
public sealed class ToolResult
{
    public required string Text { get; init; }
    public bool IsError { get; init; }
    public string? ErrorCode { get; init; }

    /// <summary>
    /// True when <see cref="Text"/> contains bytes that originated outside HeyAI's control
    /// (screen pixels, OCR output, window titles, media metadata). Tainted output flows
    /// straight into the model's context and is the project's primary injection surface.
    /// See <c>docs/SECURITY.md</c>.
    /// </summary>
    public bool Tainted { get; init; }

    public string? TaintSource { get; init; }

    /// <summary>
    /// Images to attach alongside the text. The transport emits them after the text block,
    /// so an untrusted-content banner is read before the pixels it describes.
    /// </summary>
    public IReadOnlyList<ToolImage> Images { get; init; } = [];

    public static ToolResult Ok(string text) => new() { Text = text };

    public static ToolResult Json<T>(T value) =>
        new() { Text = JsonSerializer.Serialize(value, HeyAIJson.Options) };

    /// <summary>Serialize a payload that embeds attacker-influenceable strings.</summary>
    public static ToolResult UntrustedJson<T>(T value, string source) => new()
    {
        Text = JsonSerializer.Serialize(value, HeyAIJson.Options),
        Tainted = true,
        TaintSource = source,
    };

    /// <summary>
    /// An image of something a third party controls -- the screen, a window. Always
    /// untrusted: a picture of text is still text the model will read, and an injection
    /// rendered as pixels is not fenced by anything the transport can wrap around bytes.
    /// </summary>
    public static ToolResult UntrustedImage<T>(T summary, ToolImage image, string source) =>
        new()
        {
            Text = JsonSerializer.Serialize(summary, HeyAIJson.Options),
            Tainted = true,
            TaintSource = source,
            Images = [image],
        };

    public static ToolResult Error(string code, string message) =>
        new() { Text = message, IsError = true, ErrorCode = code };

    /// <summary>
    /// An error whose message embeds attacker-influenceable text — a list of window
    /// titles to disambiguate, say.
    ///
    /// Easy to miss: fencing only the success path leaves error messages as an unfenced
    /// channel into the model's context, and an error is a particularly good place to
    /// hide an injection because it is where the model is told what to do next.
    /// </summary>
    public static ToolResult UntrustedError(string code, string message, string source) =>
        new()
        {
            Text = message,
            IsError = true,
            ErrorCode = code,
            Tainted = true,
            TaintSource = source,
        };
}

/// <summary>Every HeyAI capability exposed to an agent implements this.</summary>
public interface IHeyAITool
{
    /// <summary>Wire identifier, e.g. <c>media_get_status</c>. Stable; renaming is a breaking change.</summary>
    string Name { get; }

    /// <summary>Human-facing label for permission prompts and the tray UI.</summary>
    string Title { get; }

    /// <summary>Written for the model's tool selector, not for a human reading docs.</summary>
    string Description { get; }

    /// <summary>Strict JSON Schema. Unknown properties must be rejected.</summary>
    JsonElement InputSchema { get; }

    ToolAnnotations Annotations { get; }

    /// <summary>
    /// Classify this concrete invocation. Called before execution and before the policy
    /// engine, on every call. Must be pure, fast, and must not touch the OS.
    /// </summary>
    RiskLevel EvaluateRisk(JsonElement args);

    /// <summary>
    /// Must not throw. Operational failures come back as <see cref="ToolResult.Error"/>;
    /// only a genuine bug should ever escape, and <c>ToolInvoker</c> still catches that.
    /// </summary>
    Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct);
}

/// <summary>Convenience base: parses the schema once and defaults risk to the annotation.</summary>
public abstract class HeyAITool : IHeyAITool
{
    private readonly Lazy<JsonElement> _schema;

    protected HeyAITool() => _schema = new Lazy<JsonElement>(() => ParseSchema(SchemaJson));

    public abstract string Name { get; }
    public abstract string Title { get; }
    public abstract string Description { get; }
    public abstract ToolAnnotations Annotations { get; }

    /// <summary>Raw JSON Schema source. Kept as a literal so it is reviewable in the diff.</summary>
    protected abstract string SchemaJson { get; }

    public JsonElement InputSchema => _schema.Value;

    public virtual RiskLevel EvaluateRisk(JsonElement args) =>
        Annotations.ReadOnlyHint ? RiskLevel.Read : RiskLevel.Convenience;

    public abstract Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken ct);

    private static JsonElement ParseSchema(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    protected static string? GetString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    protected static double? GetNumber(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;

    protected static bool? GetBool(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v)
            && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;
}

public static class HeyAIJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}
