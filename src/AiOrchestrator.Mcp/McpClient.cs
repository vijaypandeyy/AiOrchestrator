using System.Text.Json;
using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Core.Options;
using AiOrchestrator.Mcp.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Mcp;

/// <summary>
/// <see cref="IMcpClient"/> implementation that speaks MCP over a <see cref="StdioMcpTransport"/>.
/// This is where JSON-RPC becomes MCP: it builds "initialize"/"tools/list"/"tools/call" requests,
/// correlates them by id, unwraps results/errors, and maps everything to the vendor-neutral
/// <see cref="AiOrchestrator.Core.Models"/> types the rest of the orchestrator uses.
/// </summary>
public sealed class McpClient : IMcpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly StdioMcpTransport _transport;
    private readonly McpOptions _options;
    private readonly ILogger<McpClient> _logger;
    private long _nextRequestId;

    public string ServerId { get; }
    public string ServerName { get; }
    public bool IsHealthy => _transport.IsHealthy;

    public McpClient(McpServerConfig config, IOptions<McpOptions> options, ILoggerFactory loggerFactory)
    {
        ServerId = config.Id;
        ServerName = config.Name;
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<McpClient>();
        _transport = new StdioMcpTransport(config, loggerFactory.CreateLogger($"AiOrchestrator.Mcp.Transport.{config.Id}"));
    }

    /// <summary>Launches the child process. Must be called before any other member.</summary>
    public void Start() => _transport.Start();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var initializeParams = new McpInitializeParams
        {
            ProtocolVersion = McpProtocolVersion.Current,
            Capabilities = JsonSerializer.SerializeToElement(new { }),
            ClientInfo = new McpClientInfo { Name = "AiOrchestrator", Version = "1.0.0" },
        };

        await SendRequestAsync<McpInitializeResult>(
            McpMethods.Initialize, initializeParams, TimeSpan.FromSeconds(_options.StartupTimeoutSeconds), cancellationToken);

        await SendNotificationAsync(McpMethods.InitializedNotification, new { }, cancellationToken);
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendRequestAsync<McpToolsListResult>(
            McpMethods.ToolsList, new { }, TimeSpan.FromSeconds(_options.StartupTimeoutSeconds), cancellationToken);

        return result.Tools
            .Select(t => new McpToolDescriptor(ServerId, ServerName, t.Name, t.Description, t.InputSchema))
            .ToList();
    }

    public async Task<McpToolCallResult> CallToolAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        using var argumentsDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
        var callParams = new McpToolsCallParams { Name = toolName, Arguments = argumentsDoc.RootElement.Clone() };

        var result = await SendRequestAsync<McpToolsCallResult>(
            McpMethods.ToolsCall, callParams, TimeSpan.FromSeconds(_options.CallTimeoutSeconds), cancellationToken);

        var text = string.Concat(result.Content.Where(c => c.Type == "text").Select(c => c.Text));
        return new McpToolCallResult(text, result.IsError);
    }

    private async Task<TResult> SendRequestAsync<TResult>(
        string method, object? @params, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!IsHealthy)
        {
            throw new InvalidOperationException($"MCP server '{ServerId}' is not healthy.");
        }

        var id = Interlocked.Increment(ref _nextRequestId);
        var paramsElement = @params is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(@params, JsonOptions);
        var request = JsonRpcMessage.Request(id, method, paramsElement);
        var pending = _transport.RegisterPending(id);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        using var registration = timeoutCts.Token.Register(() =>
            pending.TrySetException(new TimeoutException($"MCP request '{method}' to '{ServerId}' timed out after {timeout.TotalSeconds:0}s.")));

        try
        {
            await _transport.WriteLineAsync(JsonRpcCodec.EncodeLine(request), cancellationToken);
        }
        catch
        {
            _transport.CancelPending(id);
            throw;
        }

        var response = await pending.Task;

        if (response.Error is not null)
        {
            throw new McpProtocolException(ServerId, method, response.Error.Code, response.Error.Message);
        }

        if (response.Result is null)
        {
            throw new InvalidOperationException($"MCP server '{ServerId}' returned no result for '{method}'.");
        }

        return response.Result.Value.Deserialize<TResult>(JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize '{method}' result from '{ServerId}'.");
    }

    private async Task SendNotificationAsync(string method, object? @params, CancellationToken cancellationToken)
    {
        var paramsElement = @params is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(@params, JsonOptions);
        var notification = JsonRpcMessage.Notification(method, paramsElement);
        await _transport.WriteLineAsync(JsonRpcCodec.EncodeLine(notification), cancellationToken);
    }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}
