using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;

namespace AiOrchestrator.Tests.Fakes;

/// <summary>In-memory stand-in for a real <see cref="IMcpClient"/> - no process, no I/O.</summary>
public sealed class FakeMcpClient : IMcpClient
{
    private readonly List<McpToolDescriptor> _tools;
    private readonly Func<string, string, McpToolCallResult> _callHandler;

    public string ServerId { get; }
    public string ServerName { get; }
    public bool IsHealthy { get; set; } = true;
    public List<(string ToolName, string ArgumentsJson)> Calls { get; } = new();

    public FakeMcpClient(
        string serverId,
        string serverName,
        List<McpToolDescriptor> tools,
        Func<string, string, McpToolCallResult> callHandler)
    {
        ServerId = serverId;
        ServerName = serverName;
        _tools = tools;
        _callHandler = callHandler;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<McpToolDescriptor>>(_tools);

    public Task<McpToolCallResult> CallToolAsync(string toolName, string argumentsJson, CancellationToken cancellationToken = default)
    {
        Calls.Add((toolName, argumentsJson));
        return Task.FromResult(_callHandler(toolName, argumentsJson));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
