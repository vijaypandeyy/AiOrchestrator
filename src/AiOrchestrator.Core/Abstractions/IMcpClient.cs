using AiOrchestrator.Core.Models;

namespace AiOrchestrator.Core.Abstractions;

/// <summary>
/// Client-side handle to a single running MCP server process. Implementations speak the
/// MCP JSON-RPC 2.0 wire protocol (initialize / tools/list / tools/call) over whatever
/// transport the server was launched with (stdio in this solution).
/// </summary>
public interface IMcpClient : IAsyncDisposable
{
    string ServerId { get; }
    string ServerName { get; }

    /// <summary>Performs the MCP handshake ("initialize" + "notifications/initialized").</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches the tool catalog this server currently advertises.</summary>
    Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>Invokes a tool by name with the given JSON arguments payload.</summary>
    Task<McpToolCallResult> CallToolAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default);

    /// <summary>True while the underlying process is alive and the transport is usable.</summary>
    bool IsHealthy { get; }
}

/// <summary>
/// Owns the lifecycle of every configured MCP server: starts them at host startup,
/// restarts a crashed server on demand, and disposes them cleanly at shutdown.
/// </summary>
public interface IMcpClientFactory
{
    IReadOnlyCollection<IMcpClient> GetAllClients();
    IMcpClient GetClient(string serverId);
    Task StartAllAsync(CancellationToken cancellationToken = default);
    Task StopAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregates the tool catalogs of every connected MCP server into one flat list the
/// LLM provider can be given as its available "functions"/"tools" for a turn.
/// </summary>
public interface IToolCatalog
{
    Task<IReadOnlyList<McpToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>Forces a refresh of the cached catalog (e.g. after a server restarts).</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
