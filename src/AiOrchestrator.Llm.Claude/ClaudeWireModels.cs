using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiOrchestrator.Llm.Claude;

// Wire-format DTOs for Anthropic's Messages API (https://docs.claude.com/en/api/messages).
// Kept internal and separate from AiOrchestrator.Core.Models so that Claude-specific JSON
// shapes never leak past this project's ILlmProvider implementation.

internal sealed class ClaudeRequestDto
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("system")]
    public string? System { get; set; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("messages")]
    public List<ClaudeMessageDto> Messages { get; set; } = new();

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ClaudeToolDto>? Tools { get; set; }
}

internal sealed class ClaudeMessageDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public List<ClaudeContentBlockDto> Content { get; set; } = new();
}

/// <summary>
/// Flattened representation of Anthropic's polymorphic content block union
/// (type: "text" | "tool_use" | "tool_result"). Only the fields relevant to the
/// block's "type" are populated; the rest are omitted from the JSON via
/// WhenWritingNull so the wire payload matches what Anthropic expects for each type.
/// </summary>
internal sealed class ClaudeContentBlockDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Input { get; set; }

    [JsonPropertyName("tool_use_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolUseId { get; set; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolResultContent { get; set; }

    [JsonPropertyName("is_error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsError { get; set; }
}

internal sealed class ClaudeToolDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("input_schema")]
    public JsonElement InputSchema { get; set; }
}

internal sealed class ClaudeResponseDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public List<ClaudeContentBlockDto> Content { get; set; } = new();

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("usage")]
    public ClaudeUsageDto? Usage { get; set; }
}

internal sealed class ClaudeUsageDto
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}

internal sealed class ClaudeErrorEnvelopeDto
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("error")]
    public ClaudeErrorDetailDto? Error { get; set; }
}

internal sealed class ClaudeErrorDetailDto
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
