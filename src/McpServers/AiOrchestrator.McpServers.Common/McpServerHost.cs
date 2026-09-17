using System.Text.Json;
using AiOrchestrator.Mcp.Protocol;

namespace AiOrchestrator.McpServers.Common;

/// <summary>
/// Minimal, reusable MCP *server*-side runtime: reads newline-delimited JSON-RPC requests from
/// stdin, dispatches "initialize" / "tools/list" / "tools/call" / "ping" against a small
/// registry of tools, and writes JSON-RPC responses to stdout. Every domain MCP server in this
/// solution (Payee, BusinessSecurity, ApprovalWorkflow) is just this host plus a handful of
/// <see cref="McpToolRegistration"/> entries and a mocked internal API client behind them.
/// </summary>
public sealed class McpServerHost
{
    private readonly string _serverName;
    private readonly string _serverVersion;
    private readonly Dictionary<string, McpToolRegistration> _tools = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public McpServerHost(string serverName, string serverVersion = "1.0.0")
    {
        _serverName = serverName;
        _serverVersion = serverVersion;
    }

    public McpServerHost AddTool(McpToolRegistration tool)
    {
        _tools[tool.Name] = tool;
        return this;
    }

    public McpServerHost AddTool(
        string name,
        string description,
        JsonElement inputSchema,
        Func<JsonElement, CancellationToken, Task<McpToolInvocationOutcome>> handler)
    {
        return AddTool(new McpToolRegistration
        {
            Name = name,
            Description = description,
            InputSchema = inputSchema,
            Handler = handler,
        });
    }

    /// <summary>Blocks, processing requests from stdin, until stdin is closed or cancellation is requested.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        McpServerLog.Info(_serverName, $"MCP server starting with {_tools.Count} tool(s): {string.Join(", ", _tools.Keys)}");

        using var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                McpServerLog.Info(_serverName, "stdin closed; shutting down.");
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            await HandleLineAsync(line, cancellationToken);
        }
    }

    private async Task HandleLineAsync(string line, CancellationToken cancellationToken)
    {
        JsonRpcMessage? message;
        try
        {
            message = JsonRpcCodec.TryDecodeLine(line);
        }
        catch (Exception ex)
        {
            McpServerLog.Error(_serverName, $"Failed to parse incoming JSON-RPC line: {ex.Message}");
            await WriteAsync(JsonRpcMessage.ErrorResponse(null, JsonRpcErrorCodes.ParseError, "Invalid JSON."));
            return;
        }

        if (message is null)
        {
            return;
        }

        if (message.IsNotification)
        {
            if (message.Method == McpMethods.InitializedNotification)
            {
                McpServerLog.Info(_serverName, "Client completed initialization handshake.");
            }

            return; // Notifications never get a response.
        }

        if (!message.IsRequest)
        {
            return; // We never send requests to the client, so we don't expect responses either.
        }

        try
        {
            var response = message.Method switch
            {
                McpMethods.Initialize => HandleInitialize(message),
                McpMethods.ToolsList => HandleToolsList(message),
                McpMethods.ToolsCall => await HandleToolsCallAsync(message, cancellationToken),
                McpMethods.Ping => JsonRpcMessage.SuccessResponse(message.Id, JsonSerializer.SerializeToElement(new { })),
                _ => JsonRpcMessage.ErrorResponse(message.Id, JsonRpcErrorCodes.MethodNotFound, $"Unknown method '{message.Method}'."),
            };

            await WriteAsync(response);
        }
        catch (Exception ex)
        {
            McpServerLog.Error(_serverName, $"Unhandled exception processing '{message.Method}': {ex}");
            await WriteAsync(JsonRpcMessage.ErrorResponse(message.Id, JsonRpcErrorCodes.InternalError, ex.Message));
        }
    }

    private JsonRpcMessage HandleInitialize(JsonRpcMessage message)
    {
        var result = new McpInitializeResult
        {
            ProtocolVersion = McpProtocolVersion.Current,
            Capabilities = new McpServerCapabilities { Tools = new { } },
            ServerInfo = new McpClientInfo { Name = _serverName, Version = _serverVersion },
        };

        return JsonRpcMessage.SuccessResponse(message.Id, JsonSerializer.SerializeToElement(result, JsonOptions));
    }

    private JsonRpcMessage HandleToolsList(JsonRpcMessage message)
    {
        var result = new McpToolsListResult
        {
            Tools = _tools.Values
                .Select(t => new McpTool { Name = t.Name, Description = t.Description, InputSchema = t.InputSchema })
                .ToList(),
        };

        return JsonRpcMessage.SuccessResponse(message.Id, JsonSerializer.SerializeToElement(result, JsonOptions));
    }

    private async Task<JsonRpcMessage> HandleToolsCallAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        if (message.Params is null)
        {
            return JsonRpcMessage.ErrorResponse(message.Id, JsonRpcErrorCodes.InvalidParams, "Missing params for tools/call.");
        }

        var callParams = message.Params.Value.Deserialize<McpToolsCallParams>(JsonOptions);
        if (callParams is null || string.IsNullOrWhiteSpace(callParams.Name))
        {
            return JsonRpcMessage.ErrorResponse(message.Id, JsonRpcErrorCodes.InvalidParams, "Missing tool name.");
        }

        if (!_tools.TryGetValue(callParams.Name, out var tool))
        {
            return JsonRpcMessage.ErrorResponse(message.Id, JsonRpcErrorCodes.MethodNotFound, $"Unknown tool '{callParams.Name}'.");
        }

        McpToolInvocationOutcome outcome;
        try
        {
            outcome = await tool.Handler(callParams.Arguments, cancellationToken);
        }
        catch (Exception ex)
        {
            McpServerLog.Error(_serverName, $"Tool '{callParams.Name}' handler threw: {ex}");
            outcome = McpToolInvocationOutcome.Failure($"Tool execution failed: {ex.Message}");
        }

        var result = new McpToolsCallResult
        {
            IsError = outcome.IsError,
            Content = new List<McpContentItem> { new() { Type = "text", Text = outcome.Text } },
        };

        return JsonRpcMessage.SuccessResponse(message.Id, JsonSerializer.SerializeToElement(result, JsonOptions));
    }

    private static async Task WriteAsync(JsonRpcMessage message)
    {
        var line = JsonRpcCodec.EncodeLine(message);
        await Console.Out.WriteLineAsync(line);
        await Console.Out.FlushAsync();
    }
}
