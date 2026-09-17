using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Options;
using AiOrchestrator.Core.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AiOrchestrator.Mcp;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires up the MCP client stack: configuration binding, the <see cref="McpClientFactory"/>
    /// hosted service (which starts every configured server at API startup), and the
    /// aggregated <see cref="IToolCatalog"/> the orchestration loop queries.
    /// </summary>
    public static IServiceCollection AddMcpClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));

        // Registered once as a concrete singleton, then exposed under both interfaces it
        // implements via factory delegates that resolve back to that same instance - this
        // guarantees the hosted service and the injected IMcpClientFactory are one object,
        // not two independently-constructed copies.
        services.AddSingleton<McpClientFactory>();
        services.AddSingleton<IMcpClientFactory>(sp => sp.GetRequiredService<McpClientFactory>());
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<McpClientFactory>());

        services.AddSingleton<IToolCatalog, ToolCatalog>();

        return services;
    }
}
