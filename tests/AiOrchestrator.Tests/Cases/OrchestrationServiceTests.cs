using System.Text.Json;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Core.Options;
using AiOrchestrator.Core.Orchestration;
using AiOrchestrator.Tests.Fakes;
using AiOrchestrator.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Tests.Cases;

/// <summary>
/// Exercises the full "analyse -> call tool(s) -> synthesize answer" loop with a scripted
/// fake LLM and fake MCP clients - no network, no child processes, fully deterministic.
/// </summary>
public class OrchestrationServiceTests
{
    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement;

    [Fact]
    public async Task AnswersDirectlyWhenNoToolIsNeeded()
    {
        var llm = new FakeLlmProvider(
            new LlmResponse(new List<LlmContentBlock> { new TextBlock("Paris is the capital of France.") }, LlmStopReason.EndTurn));
        var factory = new FakeMcpClientFactory();
        var catalog = new ToolCatalog(factory, NullLogger<ToolCatalog>.Instance);
        var options = Options.Create(new OrchestratorOptions { MaxToolIterations = 3, SystemPrompt = "test" });
        var service = new OrchestrationService(llm, catalog, factory, options, NullLogger<OrchestrationService>.Instance);

        var response = await service.HandleQueryAsync(new QueryRequest("What is the capital of France?"));

        Assert.Equal("Paris is the capital of France.", response.Answer);
        Assert.Equal(0, response.ToolInvocations.Count);
        Assert.Equal(1, response.LlmRoundTrips);
    }

    [Fact]
    public async Task InvokesTheCorrectMcpToolThenSynthesizesAnAnswer()
    {
        var payeeTool = new McpToolDescriptor("payee", "Payee Service", "get_payee_by_id", "desc", EmptyObjectSchema);
        var payeeClient = new FakeMcpClient(
            "payee", "Payee Service", new List<McpToolDescriptor> { payeeTool },
            (_, _) => new McpToolCallResult("{\"PayeeId\":\"PAY-1001\",\"Status\":\"Active\"}", false));

        var factory = new FakeMcpClientFactory(payeeClient);
        var catalog = new ToolCatalog(factory, NullLogger<ToolCatalog>.Instance);

        var toolUseInput = JsonDocument.Parse("{\"payeeId\":\"PAY-1001\"}").RootElement;
        var firstResponse = new LlmResponse(
            new List<LlmContentBlock> { new ToolUseBlock("call-1", ToolNameCodec.Encode("payee", "get_payee_by_id"), toolUseInput) },
            LlmStopReason.ToolUse);
        var secondResponse = new LlmResponse(
            new List<LlmContentBlock> { new TextBlock("Payee PAY-1001 is Active.") },
            LlmStopReason.EndTurn);

        var llm = new FakeLlmProvider(firstResponse, secondResponse);
        var options = Options.Create(new OrchestratorOptions { MaxToolIterations = 4, SystemPrompt = "test" });
        var service = new OrchestrationService(llm, catalog, factory, options, NullLogger<OrchestrationService>.Instance);

        var response = await service.HandleQueryAsync(new QueryRequest("Is PAY-1001 active?"));

        Assert.Equal("Payee PAY-1001 is Active.", response.Answer);
        Assert.Equal(1, response.ToolInvocations.Count);
        Assert.Equal("get_payee_by_id", response.ToolInvocations[0].ToolName);
        Assert.Equal("payee", response.ToolInvocations[0].ServerId);
        Assert.False(response.ToolInvocations[0].IsError);
        Assert.Equal(2, response.LlmRoundTrips);
        Assert.Equal(1, payeeClient.Calls.Count);

        // The second LLM call must carry: original user query, the assistant's tool_use turn,
        // and a user turn with the tool_result - proving the loop feeds observations back in.
        Assert.Equal(2, llm.ReceivedRequests.Count);
        Assert.Equal(3, llm.ReceivedRequests[1].Messages.Count);
    }

    [Fact]
    public async Task ReturnsAFallbackAnswerWhenTheIterationBudgetIsExhausted()
    {
        var tool = new McpToolDescriptor("payee", "Payee Service", "get_payee_by_id", "desc", EmptyObjectSchema);
        var client = new FakeMcpClient(
            "payee", "Payee Service", new List<McpToolDescriptor> { tool },
            (_, _) => new McpToolCallResult("some data", false));
        var factory = new FakeMcpClientFactory(client);
        var catalog = new ToolCatalog(factory, NullLogger<ToolCatalog>.Instance);

        var toolUseInput = JsonDocument.Parse("{}").RootElement;
        var alwaysToolUse = Enumerable.Range(0, 3)
            .Select(_ => new LlmResponse(
                new List<LlmContentBlock> { new ToolUseBlock("id", ToolNameCodec.Encode("payee", "get_payee_by_id"), toolUseInput) },
                LlmStopReason.ToolUse))
            .ToArray();
        var llm = new FakeLlmProvider(alwaysToolUse);

        var options = Options.Create(new OrchestratorOptions { MaxToolIterations = 3, SystemPrompt = "test" });
        var service = new OrchestrationService(llm, catalog, factory, options, NullLogger<OrchestrationService>.Instance);

        var response = await service.HandleQueryAsync(new QueryRequest("loop forever please"));

        Assert.Contains("allotted number of steps", response.Answer);
        Assert.Equal(3, response.ToolInvocations.Count);
        Assert.Equal(3, response.LlmRoundTrips);
    }

    [Fact]
    public async Task RecordsAnErrorTraceWithoutThrowingWhenTheModelNamesAnUnknownTool()
    {
        var factory = new FakeMcpClientFactory(); // No servers registered at all.
        var catalog = new ToolCatalog(factory, NullLogger<ToolCatalog>.Instance);

        var toolUseInput = JsonDocument.Parse("{}").RootElement;
        var firstResponse = new LlmResponse(
            new List<LlmContentBlock> { new ToolUseBlock("id", "nonexistent__tool", toolUseInput) },
            LlmStopReason.ToolUse);
        var secondResponse = new LlmResponse(
            new List<LlmContentBlock> { new TextBlock("I could not find that information.") },
            LlmStopReason.EndTurn);

        var llm = new FakeLlmProvider(firstResponse, secondResponse);
        var options = Options.Create(new OrchestratorOptions { MaxToolIterations = 3, SystemPrompt = "test" });
        var service = new OrchestrationService(llm, catalog, factory, options, NullLogger<OrchestrationService>.Instance);

        var response = await service.HandleQueryAsync(new QueryRequest("anything"));

        Assert.Equal(1, response.ToolInvocations.Count);
        Assert.True(response.ToolInvocations[0].IsError);
        Assert.Equal("I could not find that information.", response.Answer);
    }
}
