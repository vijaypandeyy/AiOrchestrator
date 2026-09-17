namespace AiOrchestrator.Mcp.Protocol;

/// <summary>
/// MCP method names this solution implements. This is a deliberately small subset of the
/// full Model Context Protocol specification (https://modelcontextprotocol.io) - just enough
/// for a client to discover and invoke tools over stdio. Resources, prompts, sampling and the
/// HTTP+SSE transport are out of scope; see the technical document's "Hand-rolled MCP vs. the
/// official SDK" section for why and how to upgrade.
/// </summary>
public static class McpMethods
{
    public const string Initialize = "initialize";
    public const string InitializedNotification = "notifications/initialized";
    public const string ToolsList = "tools/list";
    public const string ToolsCall = "tools/call";
    public const string Ping = "ping";
}

/// <summary>The MCP protocol version this implementation speaks.</summary>
public static class McpProtocolVersion
{
    public const string Current = "2024-11-05";
}
