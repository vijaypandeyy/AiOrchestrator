using System.Text.Json;

namespace AiOrchestrator.Core.Models;

/// <summary>
/// Configuration for a single MCP server the orchestrator should launch and talk to.
/// Bound from the "Mcp:Servers" section of appsettings.json.
/// </summary>
public sealed class McpServerConfig
{
    /// <summary>Stable logical id used internally to route tool calls (e.g. "payee").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human readable name surfaced in traces / diagnostics.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Executable to launch, e.g. "dotnet".</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>Arguments passed to <see cref="Command"/>, e.g. the server dll path.</summary>
    public List<string> Args { get; set; } = new();

    /// <summary>Optional working directory for the child process.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Whether this server should be started/considered. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Extra environment variables to set on the child process.</summary>
    public Dictionary<string, string> Environment { get; set; } = new();
}

/// <summary>A tool advertised by an MCP server via "tools/list".</summary>
public sealed record McpToolDescriptor(string ServerId, string ServerName, string Name, string Description, JsonElement InputSchema);

/// <summary>Result of invoking a tool via "tools/call".</summary>
public sealed record McpToolCallResult(string TextContent, bool IsError);
