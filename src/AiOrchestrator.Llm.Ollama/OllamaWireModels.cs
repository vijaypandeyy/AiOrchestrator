using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiOrchestrator.Llm.Ollama;

// Wire-format DTOs for Ollama's native chat API (POST /api/chat, see
// https://github.com/ollama/ollama/blob/main/docs/api.md#chat-request-with-tools). Kept
// internal and separate from AiOrchestrator.Core.Models so that Ollama-specific JSON shapes
// never leak past this project's ILlmProvider implementation.
//
// Two notable differences from Anthropic's Messages API that this file's mapping code (in
// OllamaLlmProvider) has to bridge:
//   - Ollama has no per-tool-call id: a tool result is correlated by its position in the
//     conversation, not an id, so ToolUseBlock/ToolResultBlock ids are synthesized on the way
//     in and simply dropped on the way out.
//   - Tool results are their own "tool"-role messages, not a "user" message carrying
//     tool_result content blocks the way Claude models it.

internal sealed class OllamaChatRequestDto
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OllamaMessageDto> Messages { get; set; } = new();

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OllamaToolDto>? Tools { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("options")]
    public OllamaRequestOptionsDto Options { get; set; } = new();

    /// <summary>How long the model stays loaded after this request; a top-level field, not an option.</summary>
    [JsonPropertyName("keep_alive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KeepAlive { get; set; }
}

internal sealed class OllamaRequestOptionsDto
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("num_gpu")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NumGpu { get; set; }

    /// <summary>Ollama's name for the output-token budget - the equivalent of Claude's "max_tokens".</summary>
    [JsonPropertyName("num_predict")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NumPredict { get; set; }

    /// <summary>Context-window size; too small a window silently truncates the prompt.</summary>
    [JsonPropertyName("num_ctx")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NumCtx { get; set; }
}

internal sealed class OllamaMessageDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OllamaToolCallDto>? ToolCalls { get; set; }
}

internal sealed class OllamaToolCallDto
{
    [JsonPropertyName("function")]
    public OllamaToolCallFunctionDto Function { get; set; } = new();
}

internal sealed class OllamaToolCallFunctionDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public JsonElement Arguments { get; set; }
}

internal sealed class OllamaToolDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OllamaToolFunctionDto Function { get; set; } = new();
}

internal sealed class OllamaToolFunctionDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; set; }
}

internal sealed class OllamaChatResponseDto
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("message")]
    public OllamaMessageDto? Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; set; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; set; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; set; }
}

internal sealed class OllamaErrorEnvelopeDto
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
