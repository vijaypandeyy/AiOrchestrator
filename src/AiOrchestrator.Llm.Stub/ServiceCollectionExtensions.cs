using AiOrchestrator.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AiOrchestrator.Llm.Stub;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStubLlmProvider(this IServiceCollection services)
    {
        services.AddSingleton<ILlmProvider, StubLlmProvider>();
        return services;
    }
}
