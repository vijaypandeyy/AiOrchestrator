using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Mcp;

/// <summary>
/// Owns the lifecycle of every configured MCP server. Registered as an
/// <see cref="IHostedService"/> so all configured servers are launched and MCP-initialized
/// once, at API host startup, and cleanly torn down on shutdown - individual requests never
/// pay the process-startup cost.
/// </summary>
public sealed class McpClientFactory : IMcpClientFactory, IHostedService
{
    private readonly Dictionary<string, McpClient> _clients = new();
    private readonly McpOptions _options;
    private readonly IOptions<McpOptions> _optionsAccessor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpClientFactory> _logger;

    public McpClientFactory(IOptions<McpOptions> options, ILoggerFactory loggerFactory, ILogger<McpClientFactory> logger)
    {
        _optionsAccessor = options;
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public IReadOnlyCollection<IMcpClient> GetAllClients() => _clients.Values.ToList();

    public IMcpClient GetClient(string serverId) =>
        _clients.TryGetValue(serverId, out var client)
            ? client
            : throw new KeyNotFoundException($"No MCP server registered with id '{serverId}'. Check the 'Mcp:Servers' configuration.");

    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        var enabledServers = _options.Servers.Where(s => s.Enabled).ToList();
        _logger.LogInformation("Starting {Count} configured MCP server(s)...", enabledServers.Count);

        foreach (var serverConfig in enabledServers)
        {
            var client = new McpClient(serverConfig, _optionsAccessor, _loggerFactory);

            try
            {
                client.Start();
                await client.InitializeAsync(cancellationToken);
                var tools = await client.ListToolsAsync(cancellationToken);
                _clients[serverConfig.Id] = client;

                _logger.LogInformation(
                    "MCP server '{ServerId}' ({ServerName}) is ready with {ToolCount} tool(s): {Tools}",
                    serverConfig.Id, serverConfig.Name, tools.Count, string.Join(", ", tools.Select(t => t.Name)));
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to start/initialize MCP server '{ServerId}'. It will be unavailable for tool calls until the API host restarts.",
                    serverConfig.Id);

                _clients[serverConfig.Id] = client; // Registered but IsHealthy will report false.
            }
        }
    }

    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync();
        }

        _clients.Clear();
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken) => StartAllAsync(cancellationToken);

    Task IHostedService.StopAsync(CancellationToken cancellationToken) => StopAllAsync(cancellationToken);
}
