using System.Net;
using AiOrchestrator.Core.Abstractions;

namespace AiOrchestrator.Llm.Ollama;

/// <summary>Thrown when the local Ollama server returns a non-success HTTP status, or cannot be
/// reached at all (e.g. it is not running).</summary>
public sealed class OllamaApiException : LlmProviderException
{
    /// <summary>The upstream response body. Diagnostic only - never return it to an API caller.</summary>
    public string ResponseBody { get; }

    public OllamaApiException(HttpStatusCode? statusCode, string responseBody, string? message = null, Exception? innerException = null)
        : base("ollama", statusCode is null ? null : (int)statusCode, message ?? BuildDefaultMessage(statusCode, responseBody), innerException)
    {
        ResponseBody = responseBody;
    }

    private static string BuildDefaultMessage(HttpStatusCode? statusCode, string responseBody) =>
        statusCode is null
            ? $"Could not reach the Ollama server. Is it running (`ollama serve`)? Details: {responseBody}"
            : $"Ollama API request failed with status {(int)statusCode} ({statusCode}).";
}
