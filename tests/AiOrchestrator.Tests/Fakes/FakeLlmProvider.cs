using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;

namespace AiOrchestrator.Tests.Fakes;

/// <summary>Scripted <see cref="ILlmProvider"/> that returns a queued sequence of responses.</summary>
public sealed class FakeLlmProvider : ILlmProvider
{
    private readonly Queue<LlmResponse> _responses;

    public List<LlmRequest> ReceivedRequests { get; } = new();

    public FakeLlmProvider(params LlmResponse[] responses)
    {
        _responses = new Queue<LlmResponse>(responses);
    }

    public string ProviderId => "fake";

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ReceivedRequests.Add(request);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("FakeLlmProvider ran out of scripted responses.");
        }

        return Task.FromResult(_responses.Dequeue());
    }
}
