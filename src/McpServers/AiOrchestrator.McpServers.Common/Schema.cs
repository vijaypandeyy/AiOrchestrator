using System.Text.Json;

namespace AiOrchestrator.McpServers.Common;

/// <summary>Tiny helper so each server can declare its JSON-Schema tool inputs as readable literals.</summary>
public static class Schema
{
    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>A schema for a tool that takes no arguments.</summary>
    public static JsonElement Empty { get; } = Parse("""{"type":"object","properties":{}}""");
}
