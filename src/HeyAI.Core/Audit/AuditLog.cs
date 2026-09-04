using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeyAI.Core.Security;
using HeyAI.Core.Tools;

namespace HeyAI.Core.Audit;

public sealed record AuditEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Tool { get; init; }
    public required RiskLevel Risk { get; init; }
    public required PolicyOutcome Outcome { get; init; }
    public required string Reason { get; init; }

    /// <summary>SHA-256 of the raw argument JSON. Lets you correlate repeats without storing payloads.</summary>
    public required string ArgsHash { get; init; }

    /// <summary>Truncated argument JSON. Capped because tool args can carry large blobs.</summary>
    public string? Args { get; init; }

    public long? DurationMs { get; init; }
    public bool? Failed { get; init; }
    public string? ErrorCode { get; init; }
    public bool? ProducedTaintedOutput { get; init; }
    public string? Client { get; init; }
}

public interface IAuditLog
{
    void Write(AuditEntry entry);
}

/// <summary>
/// Append-only JSONL at %LOCALAPPDATA%\HeyAI\logs\audit.jsonl.
///
/// Opened with <see cref="FileMode.Append"/> and held for the process lifetime, and the
/// directory is in <see cref="HeyAIPaths.IsProtected"/> so no shell/filesystem tool can
/// reach it. Flushed on every write: a crash mid-action must still leave the record.
/// </summary>
public sealed class JsonlAuditLog : IAuditLog, IDisposable
{
    private const int MaxArgsChars = 2048;

    private readonly StreamWriter _writer;
    private readonly Lock _gate = new();

    public JsonlAuditLog(string? path = null)
    {
        path ??= HeyAIPaths.AuditLogFile;
        HeyAIPaths.EnsureCreated();
        var stream = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.Read,
            bufferSize: 4096, FileOptions.WriteThrough);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Write(AuditEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, Options);
        lock (_gate)
        {
            _writer.WriteLine(line);
        }
    }

    public static string HashArgs(string argsJson) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(argsJson)))[..16];

    public static string? TruncateArgs(string argsJson) =>
        argsJson.Length <= MaxArgsChars ? argsJson : argsJson[..MaxArgsChars] + "…[truncated]";

    public void Dispose() => _writer.Dispose();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Test/CLI sink that keeps entries in memory.</summary>
public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly List<AuditEntry> _entries = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<AuditEntry> Entries
    {
        get { lock (_gate) { return _entries.ToArray(); } }
    }

    public void Write(AuditEntry entry)
    {
        lock (_gate) { _entries.Add(entry); }
    }
}
