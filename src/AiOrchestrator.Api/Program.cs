using AiOrchestrator.Api;
using AiOrchestrator.Core.Abstractions;
using AiOrchestrator.Core.Models;
using AiOrchestrator.Core.Options;
using AiOrchestrator.Core.Orchestration;
using AiOrchestrator.Llm.Claude;
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

// --- Swagger / OpenAPI -------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "AI Orchestrator API", Version = "v1" });
});

// --- LLM provider selection --------------------------------------------------------------
// "Llm:Provider" picks the ILlmProvider implementation to register. Only Claude ships today,
// but the orchestration service depends solely on ILlmProvider, so adding e.g. an
// AzureOpenAiLlmProvider in a sibling project and a case below is the entire integration cost.
var llmProviderId = builder.Configuration.GetValue<string>("Llm:Provider") ?? "Claude";
switch (llmProviderId.Trim().ToLowerInvariant())
{
    case "claude":
        builder.Services.AddClaudeLlmProvider(builder.Configuration);
        break;
    case "stub":
        // Deterministic, offline provider for local smoke-testing without an API key.
        // See AiOrchestrator.Llm.Stub.StubLlmProvider for details - never use in production.
        builder.Services.AddStubLlmProvider();
        break;
    default:
        throw new InvalidOperationException(
            $"Unsupported Llm:Provider '{llmProviderId}'. Supported providers: Claude, Stub. " +
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
app.MapPost("/api/query", async (QueryRequest request, IOrchestrationService orchestrationService, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Query))
    {
        return Results.BadRequest(new { error = "Query must not be empty." });
    }

    try
    {
        var response = await orchestrationService.HandleQueryAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled error while processing a query.");
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.Run();
