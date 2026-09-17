using AiOrchestrator.Core.Models;

namespace AiOrchestrator.Core.Abstractions;

/// <summary>
/// Generic seam between the orchestrator and *any* LLM vendor. The orchestration loop
/// (see <see cref="IOrchestrationService"/>) only ever talks to this interface, so switching
/// from Claude to Azure OpenAI, OpenAI, Bedrock, or a local model later is purely a matter of
/// adding a new implementation and flipping the "Llm:Provider" configuration value - no
/// orchestration/business logic changes.
/// </summary>
public interface ILlmProvider
{
    /// <summary>A short id used in configuration/DI selection, e.g. "claude".</summary>
    string ProviderId { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
