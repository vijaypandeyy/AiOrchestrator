namespace AiOrchestrator.Mcp;

/// <summary>Thrown when an MCP server responds to a JSON-RPC request with an "error" object.</summary>
public sealed class McpProtocolException : Exception
{
    public string ServerId { get; }
    public string Method { get; }
    public int ErrorCode { get; }

    public McpProtocolException(string serverId, string method, int errorCode, string message)
        : base($"MCP server '{serverId}' returned error {errorCode} for method '{method}': {message}")
    {
        ServerId = serverId;
        Method = method;
        ErrorCode = errorCode;
    }
}
