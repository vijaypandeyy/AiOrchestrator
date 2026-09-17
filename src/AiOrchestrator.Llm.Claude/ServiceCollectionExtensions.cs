using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Llm.Claude;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ClaudeLlmProvider"/> as the active <see cref="ILlmProvider"/>,
    /// bound from the "Llm:Claude" configuration section, with a dedicated named
    /// <see cref="HttpClient"/> whose base address/timeout come from that same configuration.
    /// </summary>
    public static IServiceCollection AddClaudeLlmProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ClaudeOptions>(configuration.GetSection(ClaudeOptions.SectionName));

        services.AddHttpClient("ClaudeApi", (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ClaudeOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddSingleton<ILlmProvider>(provider =>
        {
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("ClaudeApi");
            var options = provider.GetRequiredService<IOptions<ClaudeOptions>>();
            var logger = provider.GetRequiredService<ILogger<ClaudeLlmProvider>>();
            return new ClaudeLlmProvider(httpClient, options, logger);
        });

        return services;
    }
}
