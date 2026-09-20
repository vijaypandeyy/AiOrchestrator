using System.Net;

namespace AiOrchestrator.Tests.Fakes;

/// <summary>
/// Captures the request body an <see cref="AiOrchestrator.Core.Abstractions.ILlmProvider"/> puts on
/// the wire and replies with a canned response, so provider mapping can be asserted without a
/// running model.
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseBody;

    public string? CapturedRequestBody { get; private set; }

    public StubHttpMessageHandler(string responseBody)
    {
        _responseBody = responseBody;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CapturedRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_responseBody),
        };
    }
}
