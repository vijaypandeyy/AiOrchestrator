using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;

namespace AiOrchestrator.Tests.Fakes;

/// <summary>
/// Stands in for an MCP client whose health probe itself blows up - the real
/// <see cref="AiOrchestrator.Mcp.StdioMcpTransport"/> used to do exactly that when its child
/// process had never started. The catalog must degrade this server to "no tools" instead of
/// failing the whole refresh.
/// </summary>
public sealed class ThrowingMcpClient : IMcpClient
{
    public string ServerId { get; }
    public string ServerName { get; }

    public bool IsHealthy => throw new InvalidOperationException("No process is associated with this object.");

    public ThrowingMcpClient(string serverId, string serverName)
    {
        ServerId = serverId;
        ServerName = serverName;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Server is not running.");

    public Task<McpToolCallResult> CallToolAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Server is not running.");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
