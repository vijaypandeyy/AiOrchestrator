using System.Reflection;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Core.Options;
using AiOrchestrator.Mcp;
using AiOrchestrator.Tests.Fakes;
using AiOrchestrator.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Tests.Cases;

public class StdioMcpTransportTests
{
    [Fact]
    public void IsHealthyIsFalseBeforeStart()
    {
        var transport = new StdioMcpTransport(EchoServerConfig(), NullLogger.Instance);
        Assert.False(transport.IsHealthy, "A transport whose process was never started must not report healthy.");
    }

    [Fact]
    public void IsHealthyIsFalseWhenTheExecutableIsMissing()
    {
        var config = new McpServerConfig
        {
            Id = "missing",
            Name = "Missing executable",
            Command = "definitely-not-an-executable-on-this-machine",
        };

        var transport = new StdioMcpTransport(config, NullLogger.Instance);

        var threw = false;
        try
        {
            transport.Start();
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert.True(threw, "Start() must surface the launch failure.");

        // The regression: Process.HasExited throws when no process was ever associated, so reading
        // IsHealthy used to bring down ToolCatalog (and with it /api/tools and /api/query).
        Assert.False(transport.IsHealthy, "An unstartable server must report unhealthy, not throw.");
    }

    [Fact]
    public async Task SurvivesAJunkLineAndAStringIdResponseFromTheServer()
    {
        await using var client = new McpClient(
            EchoServerConfig(),
            Options.Create(new McpOptions { StartupTimeoutSeconds = 20, CallTimeoutSeconds = 20 }),
            NullLoggerFactory.Instance);

        client.Start();
        Assert.True(client.IsHealthy, "A started server must report healthy.");

        // The echo server emits a non-JSON line and a response with a string id before answering
        // this one. Both are uncorrelatable frames that must be skipped rather than ending the read
        // loop - if they end it, every request below fails with a timeout instead.
        await client.InitializeAsync();

        var tools = await client.ListToolsAsync();
        Assert.Equal(1, tools.Count);
        Assert.Equal("echo", tools[0].Name);

        var result = await client.CallToolAsync("echo", "{}");
        Assert.Equal("echoed", result.TextContent);
        Assert.False(result.IsError);
    }

    [Fact]
    public async Task ATimedOutRequestReportsATimeoutAndLeavesTheServerUsable()
    {
        await using var client = CreateClient(callTimeoutSeconds: 1);
        client.Start();
        await client.InitializeAsync();

        Exception? caught = null;
        try
        {
            await client.CallToolAsync(StringIdEchoMcpServer.SilentToolName, "{}");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.True(caught is TimeoutException, $"Expected a TimeoutException but got {caught?.GetType().Name ?? "no exception"}.");

        // The abandoned request must have been dropped from the pending map, and the connection
        // must still be usable for the next call.
        var result = await client.CallToolAsync("echo", "{}");
        Assert.Equal("echoed", result.TextContent);
    }

    [Fact]
    public async Task CallerCancellationIsReportedAsCancellationNotATimeout()
    {
        await using var client = CreateClient(callTimeoutSeconds: 30);
        client.Start();
        await client.InitializeAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        Exception? caught = null;
        try
        {
            await client.CallToolAsync(StringIdEchoMcpServer.SilentToolName, "{}", cts.Token);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        // Reporting the caller's own cancellation as "the server timed out" sends whoever reads the
        // trace after the wrong problem.
        Assert.True(caught is OperationCanceledException, $"Expected an OperationCanceledException but got {caught?.GetType().Name ?? "no exception"}.");
        Assert.False(caught is TimeoutException, "Caller cancellation must not be reported as a timeout.");
    }

    private static McpClient CreateClient(int callTimeoutSeconds) => new(
        EchoServerConfig(),
        Options.Create(new McpOptions { StartupTimeoutSeconds = 20, CallTimeoutSeconds = callTimeoutSeconds }),
        NullLoggerFactory.Instance);

    /// <summary>Points at this test executable, re-launched as the stdio MCP server in Fakes.</summary>
    private static McpServerConfig EchoServerConfig()
    {
        var config = new McpServerConfig
        {
            Id = "echo",
            Name = "String-id echo server",
            Command = Environment.ProcessPath ?? "dotnet",
        };

        // Under `dotnet <assembly>.dll` the host executable is dotnet itself, so the assembly has
        // to be named explicitly; under the apphost (`dotnet run`) it must not be.
        var host = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        if (string.Equals(host, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            config.Args.Add(Assembly.GetExecutingAssembly().Location);
        }

        config.Args.Add(StringIdEchoMcpServer.Argument);
        return config;
    }
}