using AiOrchestrator.Api;
using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Core.Options;
using AiOrchestrator.Core.Orchestration;
using AiOrchestrator.Llm.Claude;
using AiOrchestrator.Llm.Ollama;
using AiOrchestrator.Llm.Stub;
using AiOrchestrator.Mcp;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

// --- Orchestration core -----------------------------------------------------------------
builder.Services.Configure<OrchestratorOptions>(builder.Configuration.GetSection(OrchestratorOptions.SectionName));
builder.Services.AddSingleton<IOrchestrationService, OrchestrationService>();

// --- Error handling ----------------------------------------------------------------------
// Every unhandled exception becomes an RFC 7807 problem response with a status code that reflects
// whose fault it is (see ApiExceptionHandler), instead of a 500 carrying raw exception text.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// --- Swagger / OpenAPI -------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "AI Orchestrator API", Version = "v1" });
});

// --- LLM provider selection --------------------------------------------------------------
// Provider selection happens before the host (and its DI container) exists, so it logs through a
// short-lived factory built from the same configuration rather than Console.WriteLine, which would
// bypass log levels, formatting and any sink the deployment configures.
using var startupLoggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
});
var startupLogger = startupLoggerFactory.CreateLogger("AiOrchestrator.Api.Startup");

// "Llm:Provider" picks the ILlmProvider implementation to register. The orchestration service
// depends solely on ILlmProvider, so adding e.g. an AzureOpenAiLlmProvider in a sibling project
// and a case below is the entire integration cost.
var llmProviderId = builder.Configuration.GetValue<string>("Llm:Provider") ?? "Claude";

if (string.Equals(llmProviderId.Trim(), "Claude", StringComparison.OrdinalIgnoreCase))
{
    // Claude needs an Anthropic API key to do anything. In Development, fall back to a
    // locally-running Ollama server so the API still starts and answers queries with zero setup or
    // cost. Anywhere else this fallback is a trap rather than a convenience: a missing key would
    // quietly change both the model and the data path (enterprise data would go to whatever Ollama
    // is reachable instead of the configured provider), so startup fails loudly instead.
    var claudeApiKeyEnvVar =
        builder.Configuration.GetValue<string>($"{ClaudeOptions.SectionName}:ApiKeyEnvironmentVariable") ?? "ANTHROPIC_API_KEY";
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(claudeApiKeyEnvVar)))
    {
        if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Llm:Provider is 'Claude' but the environment variable '{claudeApiKeyEnvVar}' is not set. " +
                $"Set it, or choose a different provider explicitly via Llm:Provider (environment " +
                $"'{builder.Environment.EnvironmentName}' does not silently fall back to Ollama).");
        }

        startupLogger.LogWarning(
            "Environment variable {EnvVar} is not set - falling back to Llm:Provider=Ollama for this Development run. " +
            "Set the key (or Llm__Provider=Stub) to use something else.",
            claudeApiKeyEnvVar);
        llmProviderId = "Ollama";
    }
}

switch (llmProviderId.Trim().ToLowerInvariant())
{
    case "claude":
        builder.Services.AddClaudeLlmProvider(builder.Configuration);
        break;
    case "ollama":
        // Local, offline-friendly provider backed by a locally-running Ollama server
        // (https://ollama.com) - no API key required. See AiOrchestrator.Llm.Ollama.OllamaLlmProvider
        // and the "Llm:Ollama" section of appsettings.json for the model/base URL it uses.
        builder.Services.AddOllamaLlmProvider(builder.Configuration);
        break;
    case "stub":
        // Deterministic, offline provider for local smoke-testing without an API key.
        // See AiOrchestrator.Llm.Stub.StubLlmProvider for details - never use in production.
        builder.Services.AddStubLlmProvider();
        break;
    default:
        throw new InvalidOperationException(
            $"Unsupported Llm:Provider '{llmProviderId}'. Supported providers: Claude, Ollama, Stub. " +
            "Implement AiOrchestrator.Core.Abstractions.ILlmProvider for a new vendor and add a case here.");
}

// --- MCP client stack ----------------------------------------------------------------------
builder.Services.AddMcpClients(builder.Configuration);

// Resolve any repository-root-relative MCP server DLL paths to absolute paths before the
// McpClientFactory hosted service launches the child processes. See SolutionPathResolver.
builder.Services.PostConfigure<McpOptions>(options =>
{
    var solutionRoot = SolutionPathResolver.TryFindSolutionRoot();
    if (solutionRoot is null)
    {
        return;
    }

    foreach (var server in options.Servers)
    {
        for (var i = 0; i < server.Args.Count; i++)
        {
            var arg = server.Args[i];
            if (arg.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !Path.IsPathRooted(arg))
            {
                server.Args[i] = Path.GetFullPath(Path.Combine(solutionRoot, arg));
            }
        }
    }
});

var app = builder.Build();

app.UseExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timeUtc = DateTimeOffset.UtcNow }));

// Debug/observability endpoint: shows exactly which tools the orchestrator currently has
// available across every connected MCP server. Handy when wiring up a new server.
app.MapGet("/api/tools", async (IToolCatalog catalog, CancellationToken cancellationToken) =>
{
    var tools = await catalog.GetToolsAsync(cancellationToken);
    return Results.Ok(tools.Select(t => new { t.ServerId, t.ServerName, t.Name, t.Description }));
});

// The single entry point: plain-English query in, synthesized answer (+ tool trace) out.
// The request is validated once, inside OrchestrationService (the invariant belongs to the domain,
// not to this transport): its ArgumentException is mapped to a 400 by ApiExceptionHandler, as every
// other failure is mapped to the status code that fits it.
app.MapPost("/api/query", async (QueryRequest request, IOrchestrationService orchestrationService, CancellationToken cancellationToken) =>
    Results.Ok(await orchestrationService.HandleQueryAsync(request, cancellationToken)));

app.Run();
