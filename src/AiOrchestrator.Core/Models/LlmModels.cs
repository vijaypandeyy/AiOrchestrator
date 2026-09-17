using System.Text.Json;

namespace AiOrchestrator.Core.Models;

/// <summary>
/// Provider-agnostic chat message. "role" is one of "user", "assistant" or "system"
/// (system is usually carried separately - see <see cref="LlmRequest.SystemPrompt"/>).
/// This shape is intentionally modelled after Anthropic's Messages API content-block
/// design because that shape is a superset of what OpenAI/Azure OpenAI-style function
/// calling needs too, which keeps <see cref="ILlmProvider"/> implementations for other
/// vendors straightforward to add later.
/// </summary>
public sealed record LlmMessage(string Role, IReadOnlyList<LlmContentBlock> Content)
{
    public static LlmMessage User(params LlmContentBlock[] content) => new("user", content);
    public static LlmMessage Assistant(params LlmContentBlock[] content) => new("assistant", content);
    public static LlmMessage UserText(string text) => new("user", new LlmContentBlock[] { new TextBlock(text) });
}

/// <summary>Base type for the polymorphic content a message can carry.</summary>
public abstract record LlmContentBlock;

/// <summary>Plain natural-language text.</summary>
public sealed record TextBlock(string Text) : LlmContentBlock;

/// <summary>Emitted by the model when it decides to invoke a tool (MCP tool call).</summary>
public sealed record ToolUseBlock(string Id, string Name, JsonElement Input) : LlmContentBlock;

/// <summary>Sent back to the model with the result of a previously requested tool call.</summary>
public sealed record ToolResultBlock(string ToolUseId, string Content, bool IsError = false) : LlmContentBlock;

/// <summary>
/// A tool definition the orchestrator exposes to the model for this turn. Each tool maps
/// 1:1 to an MCP tool advertised by one of the connected MCP servers - <see cref="ServerId"/>
/// lets the orchestrator route a requested call back to the right server.
/// </summary>
public sealed record ToolDefinition(string Name, string Description, JsonElement InputSchema, string ServerId);

/// <summary>Vendor-agnostic request passed into <see cref="ILlmProvider"/>.</summary>
public sealed record LlmRequest(
    string SystemPrompt,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools,
    int MaxTokens = 1536,
    double Temperature = 0.2);

/// <summary>Why the model stopped generating - normalized across vendors.</summary>
public enum LlmStopReason
{
    EndTurn,
    ToolUse,
    MaxTokens,
    Other
}

/// <summary>Vendor-agnostic response returned by <see cref="ILlmProvider"/>.</summary>
public sealed record LlmResponse(IReadOnlyList<LlmContentBlock> Content, LlmStopReason StopReason)
{
    public IEnumerable<ToolUseBlock> ToolUses => Content.OfType<ToolUseBlock>();
    public string TextOnly => string.Concat(Content.OfType<TextBlock>().Select(t => t.Text));
}
