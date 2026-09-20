using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiOrchestrator.Llm.Ollama;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="OllamaLlmProvider"/> as the active <see cref="ILlmProvider"/>,
    /// bound from the "Llm:Ollama" configuration section, with a dedicated named
    /// <see cref="HttpClient"/> whose base address/timeout come from that same configuration.
    /// </summary>
    public static IServiceCollection AddOllamaLlmProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));

        services.AddHttpClient("OllamaApi", (provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        services.AddSingleton<ILlmProvider>(provider =>
        {
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("OllamaApi");
            var options = provider.GetRequiredService<IOptions<OllamaOptions>>();
            var logger = provider.GetRequiredService<ILogger<OllamaLlmProvider>>();
            return new OllamaLlmProvider(httpClient, options, logger);
        });

        return services;
    }
}
