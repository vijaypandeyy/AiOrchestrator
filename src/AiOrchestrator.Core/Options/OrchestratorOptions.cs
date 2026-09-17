namespace AiOrchestrator.Core.Options;

/// <summary>Bound from the "Orchestrator" section of appsettings.json.</summary>
public sealed class OrchestratorOptions
{
    public const string SectionName = "Orchestrator";

    /// <summary>
    /// Safety valve on the reasoning loop: the maximum number of LLM round trips
    /// (think -> call tool(s) -> observe -> think again) allowed for a single query,
    /// so a confused model can never loop forever or rack up unbounded token spend.
    /// </summary>
    public int MaxToolIterations { get; set; } = 6;

    /// <summary>System prompt template used to steer intent analysis and tool selection.</summary>
    public string SystemPrompt { get; set; } =
        "You are an internal enterprise AI orchestrator. You have access to a set of tools " +
        "that call internal backend systems (payee management, business security, and approval " +
        "workflow). Analyse the user's question, decide which tool(s) - if any - can supply the " +
        "information needed, call them, and then answer the user's question in plain, concise " +
        "English using only information returned by the tools or clearly marked as general " +
        "knowledge. If no tool is relevant, answer directly. If a tool call fails, say so plainly " +
        "instead of guessing.";
}

/// <summary>Bound from the "Mcp" section of appsettings.json.</summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    public List<Models.McpServerConfig> Servers { get; set; } = new();

    /// <summary>How long to wait for a server to answer "initialize" / "tools/list" at startup.</summary>
    public int StartupTimeoutSeconds { get; set; } = 15;

    /// <summary>How long to wait for a single "tools/call" response before treating it as failed.</summary>
    public int CallTimeoutSeconds { get; set; } = 30;
}
