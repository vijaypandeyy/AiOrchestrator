using System.Text.Json;
using System.Text.RegularExpressions;
using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;

namespace AiOrchestrator.Llm.Stub;

/// <summary>
/// A deterministic, offline <see cref="ILlmProvider"/> used only for local smoke-testing the
/// end-to-end pipeline (API -&gt; orchestration loop -&gt; real MCP server child processes)
/// without an Anthropic API key or outbound network access. It applies a few keyword rules to
/// route a query to a plausible tool, then echoes back the tool's result as the "answer". It
/// is intentionally simple-minded and NOT a substitute for a real model - swap
/// "Llm:Provider" back to "Claude" (or add another <see cref="ILlmProvider"/>) for genuine
/// natural-language understanding. Select it with "Llm:Provider": "Stub" in appsettings.
/// </summary>
public sealed class StubLlmProvider : ILlmProvider
{
    private static readonly Regex IdPattern = new(@"\b(PAY|APR)-\d{3,6}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string ProviderId => "stub";

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var lastMessage = request.Messages.LastOrDefault();
        var toolResults = lastMessage?.Content.OfType<ToolResultBlock>().ToList() ?? new List<ToolResultBlock>();

        if (lastMessage is { Role: "user" } && toolResults.Count > 0)
        {
            // Second (or later) round trip: we already have tool result(s) to work with - summarize and stop.
            var summary = string.Join(
                " | ",
                toolResults.Select(r => r.IsError ? $"[tool error] {r.Content}" : r.Content));
            var text = $"[stub-llm] Based on the internal system data retrieved: {summary}";
            return Task.FromResult(new LlmResponse(new List<LlmContentBlock> { new TextBlock(text) }, LlmStopReason.EndTurn));
        }

        var userText = request.Messages
            .SelectMany(m => m.Content.OfType<TextBlock>())
            .Select(t => t.Text)
            .LastOrDefault() ?? string.Empty;

        var chosenTool = SelectTool(userText, request.Tools);
        if (chosenTool is null)
        {
            var text = "[stub-llm] No deterministic routing rule matched this question to a tool. " +
                        "Set Llm:Provider back to \"Claude\" (or add another ILlmProvider) for real reasoning over arbitrary questions.";
            return Task.FromResult(new LlmResponse(new List<LlmContentBlock> { new TextBlock(text) }, LlmStopReason.EndTurn));
        }

        var idMatch = IdPattern.Match(userText);
        var arguments = BuildArguments(chosenTool.Name, idMatch.Success ? idMatch.Value.ToUpperInvariant() : null, userText);
        var toolUse = new ToolUseBlock($"stub-{Guid.NewGuid():n}", chosenTool.Name, arguments);
        return Task.FromResult(new LlmResponse(new List<LlmContentBlock> { toolUse }, LlmStopReason.ToolUse));
    }

    private static ToolDefinition? SelectTool(string userText, IReadOnlyList<ToolDefinition> tools)
    {
        var lower = userText.ToLowerInvariant();

        if (lower.Contains("risk") || lower.Contains("fraud") || lower.Contains("security") ||
            lower.Contains("kyc") || lower.Contains("sanction"))
        {
            return tools.FirstOrDefault(t => t.ServerId == "business-security" && t.Name.EndsWith("get_security_profile"))
                ?? tools.FirstOrDefault(t => t.ServerId == "business-security");
        }

        if (lower.Contains("approv") || lower.Contains("pending"))
        {
            return tools.FirstOrDefault(t => t.ServerId == "approval-workflow" && t.Name.EndsWith("get_approval_status"))
                ?? tools.FirstOrDefault(t => t.ServerId == "approval-workflow");
        }

        if (lower.Contains("payee") || lower.Contains("vendor") || IdPattern.IsMatch(userText))
        {
            return tools.FirstOrDefault(t => t.ServerId == "payee" && t.Name.EndsWith("get_payee_by_id"))
                ?? tools.FirstOrDefault(t => t.ServerId == "payee");
        }

        return null;
    }

    private static JsonElement BuildArguments(string composedToolName, string? id, string userText)
    {
        var propertyName = composedToolName switch
        {
            var n when n.EndsWith("get_payee_by_id") => "payeeId",
            var n when n.EndsWith("get_security_profile") || n.EndsWith("check_fraud_flags") => "entityId",
            var n when n.EndsWith("get_approval_status") => "requestId",
            var n when n.EndsWith("get_approval_history_for_entity") => "entityId",
            var n when n.EndsWith("search_payees_by_name") => "name",
            _ => "id",
        };

        var arguments = new Dictionary<string, object?>();
        if (!string.IsNullOrEmpty(id))
        {
            arguments[propertyName] = id;
        }
        else if (propertyName == "name")
        {
            arguments[propertyName] = userText.Trim();
        }

        return JsonSerializer.SerializeToElement(arguments);
    }
}
