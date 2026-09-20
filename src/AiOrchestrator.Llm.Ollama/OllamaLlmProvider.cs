using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Llm.Ollama;

/// <summary>
/// <see cref="ILlmProvider"/> implementation for a locally-running Ollama server
/// (https://github.com/ollama/ollama), talking its native /api/chat endpoint. This is the
/// no-API-key fallback: it needs nothing more than Ollama running locally with a
/// tool-calling-capable model pulled (e.g. `ollama pull llama3.1`). See
/// <see cref="ServiceCollectionExtensions.AddOllamaLlmProvider"/> for how/when it is selected.
///
/// This is the only place in the solution that knows Ollama's wire format - the rest of the
/// orchestrator speaks purely in terms of <see cref="LlmRequest"/> / <see cref="LlmResponse"/>.
/// </summary>
public sealed class OllamaLlmProvider : ILlmProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaLlmProvider> _logger;

    public string ProviderId => "ollama";

    public OllamaLlmProvider(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaLlmProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var wireRequest = new OllamaChatRequestDto
        {
            Model = _options.Model,
            Messages = ToWireMessages(request),
            Tools = request.Tools.Count == 0 ? null : request.Tools.Select(ToWireTool).ToList(),
            Stream = false,
            KeepAlive = _options.KeepAlive,
            Options = new OllamaRequestOptionsDto
            {
                Temperature = request.Temperature,
                NumGpu = _options.NumGpu,
                // Without num_predict the orchestrator's token budget is silently ignored here and
                // generation is bounded only by the HTTP timeout, so a MaxTokens stop reason could
                // never fire for a limit the caller believes it set.
                NumPredict = request.MaxTokens > 0 ? request.MaxTokens : null,
                NumCtx = _options.NumCtx,
            },
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/chat");
        var payload = JsonSerializer.Serialize(wireRequest, JsonOptions);
        httpRequest.Content = new StringContent(payload, Encoding.UTF8);
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        _logger.LogDebug("Sending {MessageCount} message(s) and {ToolCount} tool(s) to Ollama model {Model}",
            wireRequest.Messages.Count, wireRequest.Tools?.Count ?? 0, wireRequest.Model);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Could not reach the Ollama server at {BaseUrl}", _httpClient.BaseAddress);
            throw new OllamaApiException(null, ex.Message, innerException: ex);
        }

        using (httpResponse)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorMessage = TryExtractErrorMessage(body) ?? body;
                _logger.LogError("Ollama API call failed with {StatusCode}: {Message}", httpResponse.StatusCode, errorMessage);
                throw new OllamaApiException(httpResponse.StatusCode, body, errorMessage);
            }

            var wireResponse = JsonSerializer.Deserialize<OllamaChatResponseDto>(body, JsonOptions)
                ?? throw new InvalidOperationException("Ollama API returned an empty response body.");

            var message = wireResponse.Message
                ?? throw new InvalidOperationException("Ollama API response did not contain a 'message'.");

            var content = FromWireMessage(message);
            var toolCallCount = message.ToolCalls?.Count ?? 0;
            var stopReason = toolCallCount > 0
                ? LlmStopReason.ToolUse
                : wireResponse.DoneReason == "length" ? LlmStopReason.MaxTokens : LlmStopReason.EndTurn;

            if (wireResponse.PromptEvalCount is not null || wireResponse.EvalCount is not null)
            {
                _logger.LogInformation(
                    "Ollama usage: {PromptTokens} prompt tokens, {EvalTokens} eval tokens",
                    wireResponse.PromptEvalCount ?? 0, wireResponse.EvalCount ?? 0);
            }

            return new LlmResponse(content, stopReason);
        }
    }

    private static string? TryExtractErrorMessage(string body)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<OllamaErrorEnvelopeDto>(body, JsonOptions);
            return envelope?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<OllamaMessageDto> ToWireMessages(LlmRequest request)
    {
        var wireMessages = new List<OllamaMessageDto>();

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            wireMessages.Add(new OllamaMessageDto { Role = "system", Content = request.SystemPrompt });
        }

        foreach (var message in request.Messages)
        {
            wireMessages.AddRange(ToWireMessages(message));
        }

        return wireMessages;
    }

    /// <summary>
    /// Unlike Claude's content-block union, Ollama models a tool result as its own "tool"-role
    /// message rather than a block inside a "user" message, and has no notion of a tool-call id
    /// to correlate a result back to the call that requested it (a result is matched to the
    /// most recent unanswered call purely by its position in the conversation). So a single
    /// <see cref="LlmMessage"/> carrying several <see cref="ToolResultBlock"/>s (one per tool the
    /// orchestrator invoked that turn) expands into several wire messages here, one per result,
    /// in the same order the orchestrator invoked them.
    /// </summary>
    private static IEnumerable<OllamaMessageDto> ToWireMessages(LlmMessage message)
    {
        if (message.Role == "assistant")
        {
            var text = string.Concat(message.Content.OfType<TextBlock>().Select(t => t.Text));
            var toolCalls = message.Content.OfType<ToolUseBlock>()
                .Select(t => new OllamaToolCallDto { Function = new OllamaToolCallFunctionDto { Name = t.Name, Arguments = t.Input } })
                .ToList();

            yield return new OllamaMessageDto
            {
                Role = "assistant",
                Content = text,
                ToolCalls = toolCalls.Count == 0 ? null : toolCalls,
            };
            yield break;
        }

        foreach (var toolResult in message.Content.OfType<ToolResultBlock>())
        {
            var content = toolResult.IsError ? $"[tool error] {toolResult.Content}" : toolResult.Content;
            yield return new OllamaMessageDto { Role = "tool", Content = content };
        }

        var userText = string.Concat(message.Content.OfType<TextBlock>().Select(t => t.Text));
        if (userText.Length > 0)
        {
            yield return new OllamaMessageDto { Role = "user", Content = userText };
        }
    }

    private static OllamaToolDto ToWireTool(ToolDefinition tool) => new()
    {
        Type = "function",
        Function = new OllamaToolFunctionDto
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = tool.InputSchema,
        },
    };

    private static List<LlmContentBlock> FromWireMessage(OllamaMessageDto message)
    {
        var blocks = new List<LlmContentBlock>();

        if (!string.IsNullOrEmpty(message.Content))
        {
            blocks.Add(new TextBlock(message.Content));
        }

        foreach (var toolCall in message.ToolCalls ?? Enumerable.Empty<OllamaToolCallDto>())
        {
            // Ollama tool calls carry no id, so one is synthesized purely so the rest of the
            // orchestrator (which is modelled after Claude's id-based correlation) has something
            // to attach the eventual ToolResultBlock to; ToWireMessages above never reads it back.
            blocks.Add(new ToolUseBlock($"ollama-{Guid.NewGuid():n}", toolCall.Function.Name, toolCall.Function.Arguments));
        }

        return blocks;
    }
}
