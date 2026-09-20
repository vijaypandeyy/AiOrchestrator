using System.Net;
using AiOrchestrator.Core.Abstractions;

namespace AiOrchestrator.Llm.Claude;

/// <summary>Thrown when the Anthropic Messages API returns a non-success HTTP status, or cannot be
/// reached at all.</summary>
public sealed class ClaudeApiException : LlmProviderException
{
    /// <summary>The upstream response body. Diagnostic only - never return it to an API caller.</summary>
    public string ResponseBody { get; }

    public ClaudeApiException(HttpStatusCode? statusCode, string responseBody, string? message = null, Exception? innerException = null)
        : base("claude", statusCode is null ? null : (int)statusCode, message ?? BuildDefaultMessage(statusCode, responseBody), innerException)
    {
        ResponseBody = responseBody;
    }

    private static string BuildDefaultMessage(HttpStatusCode? statusCode, string responseBody) =>
        statusCode is null
            ? $"Could not reach the Claude API. Details: {responseBody}"
            : $"Claude API request failed with status {(int)statusCode} ({statusCode}).";
}
