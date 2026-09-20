using System.Net;
using AiOrchestrator.Api;
using AiOrchestrator.Llm.Ollama;
using AiOrchestrator.Tests.Testing;

namespace AiOrchestrator.Tests.Cases;

/// <summary>
/// The API's job when something fails is to say whose problem it is, without handing the caller
/// internal detail. These cover the mapping that decides both.
/// </summary>
public class ApiExceptionHandlerTests
{
    [Fact]
    public void AnEmptyQueryIsABadRequestAndKeepsItsMessage()
    {
        var (status, _, detail) = ApiExceptionHandler.Map(new ArgumentException("Query must not be empty.", "request"));

        Assert.Equal(400, status);
        Assert.Contains("Query must not be empty.", detail);
    }

    [Fact]
    public void AnUnreachableModelProviderIsServiceUnavailable()
    {
        var (status, _, detail) = ApiExceptionHandler.Map(
            new OllamaApiException(null, "connection refused"));

        // An unreachable local model is not a bug in this service, and a 500 would say it was.
        Assert.Equal(503, status);
        Assert.False(detail.Contains("connection refused"), "Upstream detail must not reach the caller.");
    }

    [Fact]
    public void AThrottledModelProviderIsReportedAsTooManyRequests()
    {
        var (status, _, _) = ApiExceptionHandler.Map(
            new OllamaApiException(HttpStatusCode.TooManyRequests, "{\"error\":\"slow down\"}"));

        Assert.Equal(429, status);
    }

    [Fact]
    public void AFailingModelProviderIsABadGateway()
    {
        var (status, _, detail) = ApiExceptionHandler.Map(
            new OllamaApiException(HttpStatusCode.InternalServerError, "{\"error\":\"model 'llama9' not found\"}"));

        Assert.Equal(502, status);
        Assert.False(detail.Contains("llama9"), "The upstream response body must not reach the caller.");
    }

    [Fact]
    public void AnUpstreamTimeoutIsAGatewayTimeout()
    {
        var (status, _, _) = ApiExceptionHandler.Map(new TimeoutException("MCP request 'tools/call' timed out after 30s."));

        Assert.Equal(504, status);
    }

    [Fact]
    public void AnUnexpectedFailureIsAFiveHundredWithoutTheExceptionText()
    {
        var (status, _, detail) = ApiExceptionHandler.Map(
            new InvalidOperationException(@"C:\secrets\appsettings.json is malformed"));

        Assert.Equal(500, status);
        Assert.False(detail.Contains("secrets"), "Internal exception text must not be returned to callers.");
    }
}
