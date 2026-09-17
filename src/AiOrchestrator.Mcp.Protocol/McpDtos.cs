using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiOrchestrator.Mcp.Protocol;

// Payload shapes for the MCP methods listed in McpMethods, matching the public MCP spec
// (https://modelcontextprotocol.io/specification) closely enough that a real MCP client/
// server could interoperate with these types with only cosmetic changes.

public sealed class McpClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

public sealed class McpInitializeParams
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = McpProtocolVersion.Current;

    [JsonPropertyName("capabilities")]
    public JsonElement Capabilities { get; set; }

    [JsonPropertyName("clientInfo")]
    public McpClientInfo ClientInfo { get; set; } = new();
}

public sealed class McpServerCapabilities
{
    [JsonPropertyName("tools")]
    public object Tools { get; set; } = new { };
}

public sealed class McpInitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = McpProtocolVersion.Current;

    [JsonPropertyName("capabilities")]
    public McpServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public McpClientInfo ServerInfo { get; set; } = new();
}

public sealed class McpTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; set; }
}

public sealed class McpToolsListResult
{
    [JsonPropertyName("tools")]
    public List<McpTool> Tools { get; set; } = new();
}

public sealed class McpToolsCallParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }
}

public sealed class McpContentItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

public sealed class McpToolsCallResult
{
    [JsonPropertyName("content")]
    public List<McpContentItem> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}
