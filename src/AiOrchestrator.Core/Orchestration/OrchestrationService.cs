using System.Diagnostics;
using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Core.Orchestration;

/// <summary>
/// The heart of the system. Implements a bounded ReAct-style loop:
///
///   1. Ask the LLM to analyse the user's query, given the full catalog of MCP tools.
///   2. If it asks to call one or more tools, dispatch each call to the owning MCP server.
///   3. Feed the tool result(s) back to the LLM as observations.
///   4. Repeat until the LLM produces a final natural-language answer (or the iteration
///      budget in <see cref="OrchestratorOptions.MaxToolIterations"/> is exhausted).
///
/// This class depends only on the <see cref="ILlmProvider"/>, <see cref="IToolCatalog"/> and
/// <see cref="IMcpClientFactory"/> abstractions, so it is completely unaware of *which* LLM
/// vendor or *which* concrete MCP servers are wired up - both are swappable via DI/config.
/// </summary>
public sealed class OrchestrationService : IOrchestrationService
{
    private readonly ILlmProvider _llmProvider;
    private readonly IToolCatalog _toolCatalog;
    private readonly IMcpClientFactory _mcpClientFactory;
    private readonly OrchestratorOptions _options;
    private readonly ILogger<OrchestrationService> _logger;

    public OrchestrationService(
        ILlmProvider llmProvider,
        IToolCatalog toolCatalog,
        IMcpClientFactory mcpClientFactory,
        IOptions<OrchestratorOptions> options,
        ILogger<OrchestrationService> logger)
    {
        _llmProvider = llmProvider;
        _toolCatalog = toolCatalog;
        _mcpClientFactory = mcpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<QueryResponse> HandleQueryAsync(QueryRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException("Query must not be empty.", nameof(request));
        }

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? Guid.NewGuid().ToString("n") : request.SessionId!;
        var toolDescriptors = await _toolCatalog.GetToolsAsync(cancellationToken);
        var toolDefinitions = toolDescriptors
            .Select(d => new ToolDefinition(ToolNameCodec.Encode(d.ServerId, d.Name), d.Description, d.InputSchema, d.ServerId))
            .ToList();

        _logger.LogInformation(
            "Handling query for session {SessionId} with {ToolCount} tool(s) available", sessionId, toolDefinitions.Count);

        var messages = new List<LlmMessage> { LlmMessage.UserText(request.Query) };
        var traces = new List<ToolInvocationTrace>();
        var stepCounter = 0;
        var roundTrips = 0;

        for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
        {
            roundTrips++;
            var response = await _llmProvider.CompleteAsync(
                new LlmRequest(_options.SystemPrompt, messages, toolDefinitions),
                cancellationToken);

            if (response.StopReason != LlmStopReason.ToolUse)
            {
                return new QueryResponse(response.TextOnly, traces, roundTrips, sessionId);
            }

            // Record what the model said/decided this turn, then execute every requested tool call.
            messages.Add(new LlmMessage("assistant", response.Content));

            var resultBlocks = new List<LlmContentBlock>();
            foreach (var toolUse in response.ToolUses)
            {
                stepCounter++;
                var trace = await InvokeToolAsync(stepCounter, toolUse, cancellationToken);
                traces.Add(trace);
                resultBlocks.Add(new ToolResultBlock(toolUse.Id, trace.ResultSummary, trace.IsError));
            }

            messages.Add(new LlmMessage("user", resultBlocks));
        }

        _logger.LogWarning(
            "Session {SessionId} exhausted the {MaxIterations}-iteration tool-call budget without a final answer",
            sessionId, _options.MaxToolIterations);

        var fallback =
            "I gathered information from internal systems but was unable to finish reasoning about it within the " +
            "allotted number of steps. Please rephrase the question or narrow its scope and try again.";
        return new QueryResponse(fallback, traces, roundTrips, sessionId);
    }

    private async Task<ToolInvocationTrace> InvokeToolAsync(int step, ToolUseBlock toolUse, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!ToolNameCodec.TryDecode(toolUse.Name, out var serverId, out var toolName))
        {
            stopwatch.Stop();
            _logger.LogError("Model requested an unrecognized/unparseable tool name: {ToolName}", toolUse.Name);
            return new ToolInvocationTrace(step, "unknown", "unknown", toolUse.Name, toolUse.Input.GetRawText(),
                $"Tool '{toolUse.Name}' is not a recognized tool.", true, stopwatch.ElapsedMilliseconds);
        }

        IMcpClient client;
        try
        {
            client = _mcpClientFactory.GetClient(serverId);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "No MCP client registered for server {ServerId}", serverId);
            return new ToolInvocationTrace(step, serverId, serverId, toolName, toolUse.Input.GetRawText(),
                $"Server '{serverId}' is not available.", true, stopwatch.ElapsedMilliseconds);
        }

        try
        {
            var argumentsJson = toolUse.Input.GetRawText();
            var result = await client.CallToolAsync(toolName, argumentsJson, cancellationToken);
            stopwatch.Stop();

            _logger.LogInformation(
                "Step {Step}: called {ServerId}.{ToolName} in {DurationMs}ms (error={IsError})",
                step, serverId, toolName, stopwatch.ElapsedMilliseconds, result.IsError);

            return new ToolInvocationTrace(
                step, client.ServerId, client.ServerName, toolName, argumentsJson,
                result.TextContent, result.IsError, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Tool call {ServerId}.{ToolName} threw an exception", serverId, toolName);
            return new ToolInvocationTrace(
                step, client.ServerId, client.ServerName, toolName, toolUse.Input.GetRawText(),
                $"Tool call failed: {ex.Message}", true, stopwatch.ElapsedMilliseconds);
        }
    }
}
