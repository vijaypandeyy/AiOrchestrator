using System.Text.Json;

namespace AiOrchestrator.McpServers.Common;

/// <summary>Outcome of a single tool invocation, translated straight into an MCP "tools/call" result.</summary>
public sealed record McpToolInvocationOutcome(string Text, bool IsError = false)
{
    public static McpToolInvocationOutcome Ok(string text) => new(text, false);
    public static McpToolInvocationOutcome Failure(string text) => new(text, true);
}

/// <summary>One tool a server exposes: its MCP-visible schema plus the handler that executes it.</summary>
public sealed class McpToolRegistration
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonElement InputSchema { get; init; }
    public required Func<JsonElement, CancellationToken, Task<McpToolInvocationOutcome>> Handler { get; init; }
}
