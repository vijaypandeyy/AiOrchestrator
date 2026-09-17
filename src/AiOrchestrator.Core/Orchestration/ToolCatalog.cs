using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;
using Microsoft.Extensions.Logging;

namespace AiOrchestrator.Core.Orchestration;

/// <summary>
/// Aggregates the tool catalogs of every healthy MCP server into a single flat list,
/// caching the result until an explicit <see cref="RefreshAsync"/> is requested (e.g.
/// by the hosted service after a server restart).
/// </summary>
public sealed class ToolCatalog : IToolCatalog
{
    private readonly IMcpClientFactory _clientFactory;
    private readonly ILogger<ToolCatalog> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<McpToolDescriptor>? _cache;

    public ToolCatalog(IMcpClientFactory clientFactory, ILogger<ToolCatalog> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        await RefreshAsync(cancellationToken);
        return _cache ?? Array.Empty<McpToolDescriptor>();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var aggregated = new List<McpToolDescriptor>();

            foreach (var client in _clientFactory.GetAllClients())
            {
                if (!client.IsHealthy)
                {
                    _logger.LogWarning("Skipping tool discovery for unhealthy MCP server {ServerId}", client.ServerId);
                    continue;
                }

                try
                {
                    var tools = await client.ListToolsAsync(cancellationToken);
                    aggregated.AddRange(tools);
                    _logger.LogInformation(
                        "Discovered {Count} tool(s) from MCP server {ServerId} ({ServerName})",
                        tools.Count, client.ServerId, client.ServerName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to list tools for MCP server {ServerId}", client.ServerId);
                }
            }

            _cache = aggregated;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
