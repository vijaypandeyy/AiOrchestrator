using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Llm.Claude;

/// <summary>
/// <see cref="ILlmProvider"/> implementation for Anthropic's Claude Messages API.
/// This is the only place in the whole solution that knows Claude's wire format - the
/// rest of the orchestrator speaks purely in terms of <see cref="LlmRequest"/> /
/// <see cref="LlmResponse"/>. To add a new provider (Azure OpenAI, OpenAI, Bedrock, a
/// local model, ...) implement <see cref="ILlmProvider"/> in a sibling project and
/// register it instead of/alongside this one behind the same interface.
/// </summary>
public sealed class ClaudeLlmProvider : ILlmProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly ClaudeOptions _options;
    private readonly ILogger<ClaudeLlmProvider> _logger;

    public string ProviderId => "claude";

    public ClaudeLlmProvider(HttpClient httpClient, IOptions<ClaudeOptions> options, ILogger<ClaudeLlmProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = Environment.GetEnvironmentVariable(_options.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                $"Environment variable '{_options.ApiKeyEnvironmentVariable}' is not set. " +
                "Set it to a valid Anthropic API key before starting the API host.");
        }

        var wireRequest = new ClaudeRequestDto
        {
            Model = _options.Model,
            MaxTokens = request.MaxTokens,
            System = request.SystemPrompt,
            Temperature = request.Temperature,
            Messages = request.Messages.Select(ToWireMessage).ToList(),
            Tools = request.Tools.Count == 0 ? null : request.Tools.Select(ToWireTool).ToList(),
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "messages");
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", _options.AnthropicVersion);
        var payload = JsonSerializer.Serialize(wireRequest, JsonOptions);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8);
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        _logger.LogDebug("Sending {MessageCount} message(s) and {ToolCount} tool(s) to Claude model {Model}",
            wireRequest.Messages.Count, wireRequest.Tools?.Count ?? 0, wireRequest.Model);

        using var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var message = TryExtractErrorMessage(body) ?? body;
            _logger.LogError("Claude API call failed with {StatusCode}: {Message}", httpResponse.StatusCode, message);
            throw new ClaudeApiException(httpResponse.StatusCode, body, message);
        }

        var wireResponse = JsonSerializer.Deserialize<ClaudeResponseDto>(body, JsonOptions)
            ?? throw new InvalidOperationException("Claude API returned an empty response body.");

        var content = wireResponse.Content.Select(FromWireContent).ToList();
        var stopReason = MapStopReason(wireResponse.StopReason);

        if (wireResponse.Usage is not null)
        {
            _logger.LogInformation(
                "Claude usage: {InputTokens} input tokens, {OutputTokens} output tokens",
                wireResponse.Usage.InputTokens, wireResponse.Usage.OutputTokens);
        }

        return new LlmResponse(content, stopReason);
    }

    private static string? TryExtractErrorMessage(string body)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<ClaudeErrorEnvelopeDto>(body, JsonOptions);
            return envelope?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LlmStopReason MapStopReason(string? stopReason) => stopReason switch
    {
        "tool_use" => LlmStopReason.ToolUse,
        "end_turn" => LlmStopReason.EndTurn,
        "stop_sequence" => LlmStopReason.EndTurn,
        "max_tokens" => LlmStopReason.MaxTokens,
        _ => LlmStopReason.Other,
    };

    private static ClaudeToolDto ToWireTool(ToolDefinition tool) => new()
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = tool.InputSchema,
    };

    private static ClaudeMessageDto ToWireMessage(LlmMessage message) => new()
    {
        Role = message.Role,
        Content = message.Content.Select(ToWireContent).ToList(),
    };

    private static ClaudeContentBlockDto ToWireContent(LlmContentBlock block) => block switch
    {
        TextBlock text => new ClaudeContentBlockDto { Type = "text", Text = text.Text },
        ToolUseBlock toolUse => new ClaudeContentBlockDto
        {
            Type = "tool_use",
            Id = toolUse.Id,
            Name = toolUse.Name,
            Input = toolUse.Input,
        },
        ToolResultBlock toolResult => new ClaudeContentBlockDto
        {
            Type = "tool_result",
            ToolUseId = toolResult.ToolUseId,
            ToolResultContent = toolResult.Content,
            IsError = toolResult.IsError ? true : null,
        },
        _ => throw new NotSupportedException($"Unsupported content block type: {block.GetType().Name}"),
    };

    private static LlmContentBlock FromWireContent(ClaudeContentBlockDto dto) => dto.Type switch
    {
        "text" => new TextBlock(dto.Text ?? string.Empty),
        "tool_use" => new ToolUseBlock(dto.Id ?? string.Empty, dto.Name ?? string.Empty, dto.Input ?? default),
        _ => new TextBlock(string.Empty),
    };
}
