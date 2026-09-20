namespace AiOrchestrator.Core.Abstractions;

/// <summary>
/// Vendor-neutral base for "the call to the language model provider failed". Each
/// <see cref="ILlmProvider"/> implementation derives its own exception from this, so callers -
/// the API's error mapping in particular - can tell an unreachable or failing *upstream* apart
/// from a bug in this service without referencing any vendor's project or exception type.
/// </summary>
public abstract class LlmProviderException : Exception
{
    /// <summary>Matches <see cref="ILlmProvider.ProviderId"/>, e.g. "claude" or "ollama".</summary>
    public string ProviderId { get; }

    /// <summary>The upstream HTTP status, or null when the provider could not be reached at all.</summary>
    public int? StatusCode { get; }

    /// <summary>True when the provider never answered (wrong URL, service down, network failure).</summary>
    public bool IsUnreachable => StatusCode is null;

    /// <summary>True when the provider asked us to slow down (HTTP 429) or is temporarily overloaded.</summary>
    public bool IsThrottled => StatusCode is 429 or 529;

    protected LlmProviderException(string providerId, int? statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderId = providerId;
        StatusCode = statusCode;
    }
}
