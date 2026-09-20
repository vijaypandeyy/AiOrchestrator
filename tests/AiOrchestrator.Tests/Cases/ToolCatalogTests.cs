using System.Text.Json;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Core.Options;
using AiOrchestrator.Core.Orchestration;
using AiOrchestrator.Tests.Fakes;
using AiOrchestrator.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Tests.Cases;

public class ToolCatalogTests
{
    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement;

    [Fact]
    public async Task OneBrokenServerDoesNotFailTheWholeCatalog()
    {
        var healthy = new FakeMcpClient(
            "payee",
            "Payee Server",
            new List<McpToolDescriptor> { new("payee", "Payee Server", "get_payee", "Gets a payee.", EmptyObjectSchema) },
            (_, _) => new McpToolCallResult("{}", false));

        var catalog = CreateCatalog(new FakeMcpClientFactory(healthy, new ThrowingMcpClient("broken", "Broken Server")));

        var tools = await catalog.GetToolsAsync();

        Assert.Equal(1, tools.Count);
        Assert.Equal("get_payee", tools[0].Name);
    }

    [Fact]
    public async Task AnEmptyCatalogIsNeverCached()
    {
        var server = PayeeServer();
        server.IsHealthy = false; // Down when the first query arrives.
        var catalog = CreateCatalog(new FakeMcpClientFactory(server));

        Assert.Equal(0, (await catalog.GetToolsAsync()).Count);

        // Caching the empty list would leave the model with no tools, and the grounding guardrail
        // would then refuse every query until the API host restarted.
        server.IsHealthy = true;
        Assert.Equal(1, (await catalog.GetToolsAsync()).Count);
    }

    [Fact]
    public async Task ANonEmptyCatalogIsReusedWithinItsTtl()
    {
        var server = PayeeServer();
        var catalog = CreateCatalog(new FakeMcpClientFactory(server), ttlSeconds: 300);

        await catalog.GetToolsAsync();
        await catalog.GetToolsAsync();

        Assert.Equal(1, server.ListToolsCallCount);
    }

    [Fact]
    public async Task ACatalogWithoutATtlIsRebuiltOnEveryQuery()
    {
        var server = PayeeServer();
        var catalog = CreateCatalog(new FakeMcpClientFactory(server), ttlSeconds: 0);

        await catalog.GetToolsAsync();
        await catalog.GetToolsAsync();

        Assert.Equal(2, server.ListToolsCallCount);
    }

    private static ToolCatalog CreateCatalog(FakeMcpClientFactory factory, int ttlSeconds = 300) => new(
        factory,
        Options.Create(new OrchestratorOptions { ToolCatalogTtlSeconds = ttlSeconds }),
        NullLogger<ToolCatalog>.Instance);

    private static FakeMcpClient PayeeServer() => new(
        "payee",
        "Payee Server",
        new List<McpToolDescriptor> { new("payee", "Payee Server", "get_payee", "Gets a payee.", EmptyObjectSchema) },
        (_, _) => new McpToolCallResult("{}", false));
}
