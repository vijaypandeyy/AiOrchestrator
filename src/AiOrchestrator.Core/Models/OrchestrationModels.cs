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
    string SessionId);
