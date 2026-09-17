namespace AiOrchestrator.Core.Options;

/// <summary>Bound from the "Llm:Claude" section of appsettings.json.</summary>
public sealed class ClaudeOptions
{
    public const string SectionName = "Llm:Claude";

    /// <summary>Anthropic Messages API base url.</summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/";

    /// <summary>Model id, e.g. "claude-sonnet-4-5-20250929".</summary>
    public string Model { get; set; } = "claude-sonnet-4-5-20250929";

    /// <summary>Anthropic API version header value.</summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";

    /// <summary>
    /// Name of the environment variable that holds the API key. The key itself is never
    /// stored in appsettings.json / source control - only the *name* of the env var is.
    /// </summary>
    public string ApiKeyEnvironmentVariable { get; set; } = "ANTHROPIC_API_KEY";

    public int TimeoutSeconds { get; set; } = 60;
}
