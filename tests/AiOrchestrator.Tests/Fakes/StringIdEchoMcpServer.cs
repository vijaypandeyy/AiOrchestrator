using System.Text.Json;
using AiOrchestrator.Mcp.Protocol;

namespace AiOrchestrator.Tests.Fakes;

/// <summary>
/// A minimal MCP server over stdio, hosted inside the test executable itself (see
/// <c>Program.cs</c>: it runs when the process is launched with <see cref="Argument"/>) so
/// <see cref="AiOrchestrator.Mcp.StdioMcpTransport"/> can be exercised against a real child
/// process without depending on one of the solution's domain servers.
///
/// Before its first real response it deliberately emits two frames no well-behaved server would
/// send: a line that is not JSON at all, and a JSON-RPC response carrying a *string* id that
/// matches no pending request. Both must be logged and skipped; if either one ends the transport's
/// read loop, every request afterwards fails, which is exactly the regression this server guards.
/// </summary>
public static class StringIdEchoMcpServer
{
    public const string Argument = "--string-id-echo-mcp-server";

    /// <summary>A tools/call for this tool is read and then deliberately never answered, so tests can
    /// exercise the client's request timeout and caller-cancellation paths.</summary>
    public const string SilentToolName = "never-responds";

    public static int Run()
    {
        var noiseEmitted = false;
        string? line;

        while ((line = Console.In.ReadLine()) is not null)
        {
            JsonRpcMessage? message;
            try
            {
                message = JsonRpcCodec.TryDecodeLine(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (message is null || !message.IsRequest)
            {
                continue; // Notifications (e.g. notifications/initialized) get no response.
            }

            if (!noiseEmitted)
            {
                noiseEmitted = true;
                Console.Out.WriteLine("{ this line is not valid JSON");
                Console.Out.WriteLine(JsonRpcCodec.EncodeLine(JsonRpcMessage.SuccessResponse(
                    JsonSerializer.SerializeToElement("a-string-request-id"),
                    JsonSerializer.SerializeToElement(new { note = "response for a request this client never sent" }))));
            }

            if (IsCallOf(message, SilentToolName))
            {
                continue; // Swallow the request: the client must time out (or be cancelled) on its own.
            }

            Console.Out.WriteLine(JsonRpcCodec.EncodeLine(Respond(message)));
        }

        return 0;
    }

    private static bool IsCallOf(JsonRpcMessage request, string toolName) =>
        request.Method == McpMethods.ToolsCall
        && request.Params.HasValue
        && request.Params.Value.TryGetProperty("name", out var name)
        && name.GetString() == toolName;

    private static JsonRpcMessage Respond(JsonRpcMessage request) => request.Method switch
    {
        McpMethods.Initialize => JsonRpcMessage.SuccessResponse(
            request.Id,
            JsonSerializer.SerializeToElement(new McpInitializeResult
            {
                ServerInfo = new McpClientInfo { Name = "string-id-echo", Version = "1.0.0" },
            })),

        McpMethods.ToolsList => JsonRpcMessage.SuccessResponse(
            request.Id,
            JsonSerializer.SerializeToElement(new McpToolsListResult
            {
                Tools =
                {
                    new McpTool
                    {
                        Name = "echo",
                        Description = "Echoes its argument back.",
                        InputSchema = JsonSerializer.SerializeToElement(new { type = "object" }),
                    },
                },
            })),

        McpMethods.ToolsCall => JsonRpcMessage.SuccessResponse(
            request.Id,
            JsonSerializer.SerializeToElement(new McpToolsCallResult
            {
                Content = { new McpContentItem { Type = "text", Text = "echoed" } },
            })),

        _ => JsonRpcMessage.ErrorResponse(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Unknown method '{request.Method}'."),
    };
}
