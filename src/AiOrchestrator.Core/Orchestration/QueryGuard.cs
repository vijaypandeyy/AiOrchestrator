using System.Text.RegularExpressions;
using AiOrchestrator.Core.Options;

namespace AiOrchestrator.Core.Orchestration;

/// <summary>
/// Stateless, deterministic checks that keep the orchestrator inside its intended scope
/// (payee / business-security / approval-workflow questions). Runs in code before and after the
/// LLM so the boundary does not depend on the model choosing to follow its system prompt.
/// </summary>
public static class QueryGuard
{
    // Phrases that show up in prompt-injection / jailbreak attempts and have no place in a
    // legitimate payee, security or approval question.
    private static readonly Regex InjectionPattern = new(
        @"ignore\s+(all\s+|any\s+|the\s+)?(previous|prior|above|earlier)\s+(instructions|rules|prompts?)|" +
        @"disregard\s+(all\s+|any\s+|the\s+)?(previous|prior|above|earlier|your)\s+(instructions|rules)|" +
        @"(reveal|show|print|repeat)\s+(your\s+|the\s+)?(system\s+prompt|instructions)|" +
        @"you\s+are\s+now\s+|jailbreak|developer\s+mode|\bDAN\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Checks the raw user query before any LLM call. Returns the message to send back to the
    /// caller when the query must be refused, or null when it may proceed.
    /// </summary>
    public static string? CheckQuery(string query, GuardrailOptions options)
    {
        if (!options.Enabled)
        {
            return null;
        }

        if (query.Length > options.MaxQueryLength)
        {
            return $"Your question is too long ({query.Length} characters). Please keep it under " +
                   $"{options.MaxQueryLength} characters and focus on a single payee, security or approval question.";
        }

        if (options.BlockCodeInAnswers && ContainsCodeFence(query))
        {
            return options.OutOfScopeMessage;
        }

        if (options.BlockPromptInjectionPhrases && InjectionPattern.IsMatch(query))
        {
            return options.OutOfScopeMessage;
        }

        return null;
    }

    /// <summary>
    /// Checks the model's final answer. Returns the replacement message when the answer must be
    /// withheld, or null when it may be returned as is.
    /// </summary>
    public static string? CheckAnswer(string answer, int toolCallCount, GuardrailOptions options)
    {
        if (!options.Enabled)
        {
            return null;
        }

        if (options.RequireToolGrounding && toolCallCount == 0)
        {
            return options.OutOfScopeMessage;
        }

        if (options.BlockCodeInAnswers && ContainsCodeFence(answer))
        {
            return options.OutOfScopeMessage;
        }

        return null;
    }

    private static bool ContainsCodeFence(string text) => text.Contains("```", StringComparison.Ordinal);
}
