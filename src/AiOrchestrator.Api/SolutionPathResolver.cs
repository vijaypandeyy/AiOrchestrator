namespace AiOrchestrator.Api;

/// <summary>
/// Local-development convenience only: lets "Mcp:Servers[].Args" in appsettings.json reference
/// MCP server DLLs with paths relative to the repository root (e.g.
/// "src/McpServers/PayeeMcpServer/bin/Debug/net8.0/PayeeMcpServer.dll") instead of forcing every
/// developer to hardcode an absolute path. It walks up from the running assembly's directory
/// until it finds the .sln file. In a non-local deployment you would instead publish each MCP
/// server and point "Command"/"Args" at its published, absolute location - this resolver simply
/// never triggers in that case because the configured path is already rooted.
/// </summary>
public static class SolutionPathResolver
{
    public static string? TryFindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.GetFiles("*.sln").Length > 0)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
