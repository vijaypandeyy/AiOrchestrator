using System.Text.Json;

namespace AiOrchestrator.Mcp.Protocol;

/// <summary>
/// Encodes/decodes <see cref="JsonRpcMessage"/> instances as single lines of newline-delimited
/// JSON, which is exactly what the MCP stdio transport spec requires: each message is a
/// complete JSON value on its own line, with no embedded newlines.
/// </summary>
public static class JsonRpcCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string EncodeLine(JsonRpcMessage message) => JsonSerializer.Serialize(message, Options);

    public static JsonRpcMessage? TryDecodeLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        return JsonSerializer.Deserialize<JsonRpcMessage>(line, Options);
    }
}
