using AiOrchestrator.Core.Models;

namespace AiOrchestrator.Core.Abstractions;

/// <summary>
/// The single entry point the API layer calls. Implementations run the full
/// "analyse intent -> select and invoke MCP tool(s) -> synthesize answer" loop.
/// </summary>
public interface IOrchestrationService
{
    Task<QueryResponse> HandleQueryAsync(QueryRequest request, CancellationToken cancellationToken = default);
}
