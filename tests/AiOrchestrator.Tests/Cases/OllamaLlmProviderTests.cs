using System.Text.Json;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Core.Options;
using AiOrchestrator.Llm.Ollama;
using AiOrchestrator.Tests.Fakes;
using AiOrchestrator.Tests.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Tests.Cases;

public class OllamaLlmProviderTests
{
    private const string DoneResponse =
        "{\"model\":\"llama3.1\",\"message\":{\"role\":\"assistant\",\"content\":\"hello\"},\"done\":true,\"done_reason\":\"stop\"}";

    [Fact]
    public async Task SendsTheTokenBudgetAsNumPredict()
    {
        var (provider, handler) = CreateProvider(DoneResponse);

        await provider.CompleteAsync(new LlmRequest(
            SystemPrompt: "system",
            Messages: new List<LlmMessage> { new("user", new List<LlmContentBlock> { new TextBlock("hi") }) },
            Tools: new List<ToolDefinition>(),
            MaxTokens: 512));

        var options = JsonDocument.Parse(handler.CapturedRequestBody!).RootElement.GetProperty("options");

        // Without this the orchestrator's budget was silently dropped and generation was bounded
        // only by the HTTP timeout.
        Assert.Equal(512, options.GetProperty("num_predict").GetInt32());
    }

    [Fact]
    public async Task SendsTheConfiguredContextWindowAndKeepAlive()
    {
        var (provider, handler) = CreateProvider(DoneResponse, new OllamaOptions
        {
            Model = "llama3.1",
            NumCtx = 8192,
            KeepAlive = "30m",
        });

        await provider.CompleteAsync(new LlmRequest(
            SystemPrompt: "system",
            Messages: new List<LlmMessage> { new("user", new List<LlmContentBlock> { new TextBlock("hi") }) },
            Tools: new List<ToolDefinition>()));

        var request = JsonDocument.Parse(handler.CapturedRequestBody!).RootElement;

        Assert.Equal(8192, request.GetProperty("options").GetProperty("num_ctx").GetInt32());
        Assert.Equal("30m", request.GetProperty("keep_alive").GetString());
    }

    [Fact]
    public async Task OmitsUnsetOllamaTuningOptions()
    {
        var (provider, handler) = CreateProvider(DoneResponse);

        await provider.CompleteAsync(new LlmRequest(
            SystemPrompt: "system",
            Messages: new List<LlmMessage> { new("user", new List<LlmContentBlock> { new TextBlock("hi") }) },
            Tools: new List<ToolDefinition>()));

        var request = JsonDocument.Parse(handler.CapturedRequestBody!).RootElement;

        // Sending nulls would override Ollama's own defaults with nothing useful.
        Assert.False(request.TryGetProperty("keep_alive", out _), "keep_alive must be omitted when unset.");
        Assert.False(request.GetProperty("options").TryGetProperty("num_ctx", out _), "num_ctx must be omitted when unset.");
    }

    [Fact]
    public async Task MapsALengthStopToMaxTokens()
    {
        var (provider, _) = CreateProvider(
            "{\"model\":\"llama3.1\",\"message\":{\"role\":\"assistant\",\"content\":\"trunc\"},\"done\":true,\"done_reason\":\"length\"}");

        var response = await provider.CompleteAsync(new LlmRequest(
            SystemPrompt: "system",
            Messages: new List<LlmMessage> { new("user", new List<LlmContentBlock> { new TextBlock("hi") }) },
            Tools: new List<ToolDefinition>()));

        Assert.Equal(LlmStopReason.MaxTokens, response.StopReason);
    }

    private static (OllamaLlmProvider Provider, StubHttpMessageHandler Handler) CreateProvider(
        string responseBody, OllamaOptions? options = null)
    {
        var handler = new StubHttpMessageHandler(responseBody);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434/") };
        var provider = new OllamaLlmProvider(
            httpClient,
            Options.Create(options ?? new OllamaOptions { Model = "llama3.1" }),
            NullLogger<OllamaLlmProvider>.Instance);

        return (provider, handler);
    }
}
