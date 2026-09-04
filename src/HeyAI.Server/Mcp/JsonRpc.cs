using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeyAI.Server.Mcp;

// Minimal JSON-RPC 2.0 types for MCP over stdio.
//
// Hand-rolled rather than taking the ModelContextProtocol SDK: this server's value is the
// Windows surface, and every call has to pass through ToolInvoker for the policy and
// audit pipeline. An SDK's attribute-based tool registration would fight that, and the
// transport itself is one file. Revisit if HeyAI ever needs sampling or elicitation.

public sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public JsonElement? Id { get; set; }
    [JsonPropertyName("method")] public string Method { get; set; } = "";
    [JsonPropertyName("params")] public JsonElement? Params { get; set; }

    /// <summary>A request without an id is a notification and must not be answered.</summary>
    [JsonIgnore]
    public bool IsNotification => Id is null || Id.Value.ValueKind == JsonValueKind.Null;
}

public sealed class JsonRpcError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";

    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}

public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public JsonElement? Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcError? Error { get; set; }

    public static JsonRpcResponse Ok(JsonElement? id, object result) =>
        new() { Id = id, Result = result };

    public static JsonRpcResponse Fail(JsonElement? id, int code, string message) =>
        new() { Id = id, Error = new JsonRpcError { Code = code, Message = message } };
}
