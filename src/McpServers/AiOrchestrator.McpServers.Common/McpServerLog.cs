namespace AiOrchestrator.McpServers.Common;

/// <summary>
/// Diagnostic logging for an MCP server process. Writes exclusively to stderr, because the
/// MCP stdio transport reserves stdout for JSON-RPC frames only - anything else written to
/// stdout would corrupt the protocol stream from the orchestrator's point of view.
/// </summary>
public static class McpServerLog
{
    public static void Info(string serverName, string message) => Write(serverName, "INFO", message);

    public static void Warn(string serverName, string message) => Write(serverName, "WARN", message);

    public static void Error(string serverName, string message) => Write(serverName, "ERROR", message);

    private static void Write(string serverName, string level, string message) =>
        Console.Error.WriteLine($"{DateTimeOffset.UtcNow:HH:mm:ss.fff} [{level}] {serverName}: {message}");
}
