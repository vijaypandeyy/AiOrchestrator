using System.Net;

namespace AiOrchestrator.Llm.Claude;

/// <summary>Thrown when the Anthropic Messages API returns a non-success HTTP status.</summary>
public sealed class ClaudeApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }

    public ClaudeApiException(HttpStatusCode statusCode, string responseBody, string? message = null)
        : base(message ?? $"Claude API request failed with status {(int)statusCode} ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
