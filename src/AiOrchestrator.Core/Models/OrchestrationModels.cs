namespace AiOrchestrator.Core.Models;

/// <summary>Request body for POST /api/query.</summary>
public sealed record QueryRequest(string Query, string? SessionId = null);

/// <summary>One tool invocation the orchestrator made while answering a query, for transparency/audit.</summary>
public sealed record ToolInvocationTrace(
    int Step,
    string ServerId,
    string ServerName,
    string ToolName,
    string ArgumentsJson,
    string ResultSummary,
    bool IsError,
    long DurationMs);

/// <summary>Response body for POST /api/query.</summary>
public sealed record QueryResponse(
    string Answer,
    IReadOnlyList<ToolInvocationTrace> ToolInvocations,
    int LlmRoundTrips,
    string SessionId,
    bool Refused = false,
    /// <summary>True when the model hit its output-token budget, so <see cref="Answer"/> is incomplete.
    /// Callers should not treat a truncated answer as a finished one.</summary>
    bool Truncated = false);
