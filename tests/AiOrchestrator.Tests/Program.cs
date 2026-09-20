using AiOrchestrator.Tests.Fakes;
using AiOrchestrator.Tests.Testing;

// The same executable doubles as a test MCP server when launched with this argument, so the
// transport tests can talk to a real child process (see StringIdEchoMcpServer).
if (args.Length > 0 && args[0] == StringIdEchoMcpServer.Argument)
{
    return StringIdEchoMcpServer.Run();
}

return await TestRunner.RunAllAsync();
