using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Core.Orchestration;

/// <summary>
/// Aggregates the tool catalogs of every healthy MCP server into a single flat list and caches the
/// result for <see cref="OrchestratorOptions.ToolCatalogTtlSeconds"/>, so a server that was down at
/// startup - or that crashed and came back - rejoins the catalog on its own instead of staying
/// invisible until the API host restarts.
///
/// An *empty* aggregate is deliberately never cached. Caching it would be the worst possible
/// outcome: with no tools to offer, the model cannot ground any answer, and the grounding guardrail
/// would then refuse every query for as long as the empty list stayed cached.
/// </summary>
public sealed class ToolCatalog : IToolCatalog
{
    private readonly IMcpClientFactory _clientFactory;
    private readonly OrchestratorOptions _options;
    private readonly ILogger<ToolCatalog> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private IReadOnlyList<McpToolDescriptor>? _cache;
    private DateTimeOffset _cachedAtUtc;

    public ToolCatalog(IMcpClientFactory clientFactory, IOptions<OrchestratorOptions> options, ILogger<ToolCatalog> logger)
    {
        _clientFactory = clientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        var cached = _cache;
        if (cached is { Count: > 0 } && !IsStale())
        {
            return cached;
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
                // The whole per-client block is guarded: IsHealthy is an implementation detail of
                // each client and a faulty one must degrade this server to "no tools", never fail
                // the catalog (and with it /api/tools and /api/query) for all the others.
                try
                {
                    if (!client.IsHealthy)
                    {
                        _logger.LogWarning("Skipping tool discovery for unhealthy MCP server {ServerId}", client.ServerId);
                        continue;
                    }

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

            if (aggregated.Count == 0)
            {
                _logger.LogWarning(
                    "No MCP server returned any tools; keeping the previous catalog ({Count} tool(s)) and retrying on the next query",
                    _cache?.Count ?? 0);
                return;
            }

            _cache = aggregated;
            _cachedAtUtc = DateTimeOffset.UtcNow;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsStale()
    {
        var ttlSeconds = _options.ToolCatalogTtlSeconds;
        return ttlSeconds <= 0 || DateTimeOffset.UtcNow - _cachedAtUtc >= TimeSpan.FromSeconds(ttlSeconds);
    }
}
