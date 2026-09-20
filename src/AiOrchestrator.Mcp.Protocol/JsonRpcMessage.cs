using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiOrchestrator.Mcp.Protocol;

/// <summary>
/// A single flattened JSON-RPC 2.0 envelope used for every message direction (request,
/// response, notification). Which fields are populated tells you which kind of message
/// it is - that is standard JSON-RPC 2.0 discrimination and needs no custom polymorphic
/// converter:
///   - request:      Method + Id are set, Result/Error are not
///   - notification: Method is set, Id is not
///   - response:     Method is not set, exactly one of Result/Error is set, Id echoes the request
/// </summary>
public sealed class JsonRpcMessage
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcErrorPayload? Error { get; set; }

    [JsonIgnore]
    public bool IsRequest => Method is not null && Id is not null;

    [JsonIgnore]
    public bool IsNotification => Method is not null && Id is null;

    [JsonIgnore]
    public bool IsResponse => Method is null && (Result is not null || Error is not null);

    /// <summary>
    /// The id as a correlation key: its raw JSON text, so the numeric id <c>7</c> and the string id
    /// <c>"7"</c> stay distinct. JSON-RPC 2.0 allows both, and MCP servers in the wild use string ids,
    /// so correlation must never assume a number. Null when there is no id, or when it is neither a
    /// number nor a string (which the spec does not permit and which cannot be correlated).
    /// </summary>
    [JsonIgnore]
    public string? IdKey =>
        Id is { ValueKind: JsonValueKind.Number or JsonValueKind.String } id ? id.GetRawText() : null;

    public static JsonRpcMessage Request(long id, string method, JsonElement? @params) => new()
    {
        Id = JsonSerializer.SerializeToElement(id),
        Method = method,
        Params = @params,
    };

    public static JsonRpcMessage Notification(string method, JsonElement? @params) => new()
    {
        Method = method,
        Params = @params,
    };

    public static JsonRpcMessage SuccessResponse(JsonElement? id, JsonElement result) => new()
    {
        Id = id,
        Result = result,
    };

    public static JsonRpcMessage ErrorResponse(JsonElement? id, int code, string message) => new()
    {
        Id = id,
        Error = new JsonRpcErrorPayload { Code = code, Message = message },
    };
}

public sealed class JsonRpcErrorPayload
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }
}

/// <summary>Standard JSON-RPC 2.0 error codes used by the MCP servers in this solution.</summary>
public static class JsonRpcErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
}
