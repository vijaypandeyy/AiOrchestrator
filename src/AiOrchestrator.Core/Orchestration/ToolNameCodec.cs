namespace AiOrchestrator.Core.Orchestration;

/// <summary>
/// MCP tool names are only unique *within* a server, and two different domain servers could
/// both expose e.g. "get_status". The LLM, however, is given one flat namespace of tools, so
/// every tool is exposed to it as "{serverId}__{toolName}" and decoded back on the way in.
/// Anthropic (and most vendors) restrict tool names to [a-zA-Z0-9_-]{1,128}, which is why
/// "__" is used as the separator instead of a character like '.'.
/// </summary>
public static class ToolNameCodec
{
    private const string Separator = "__";

    public static string Encode(string serverId, string toolName) => $"{serverId}{Separator}{toolName}";

    public static bool TryDecode(string composedName, out string serverId, out string toolName)
    {
        var idx = composedName.IndexOf(Separator, StringComparison.Ordinal);
        if (idx <= 0 || idx == composedName.Length - Separator.Length)
        {
            serverId = string.Empty;
            toolName = string.Empty;
            return false;
        }

        serverId = composedName[..idx];
        toolName = composedName[(idx + Separator.Length)..];
        return true;
    }
}
