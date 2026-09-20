# AI Orchestrator

A .NET 8 API that takes a plain-English question, decides which internal backend system(s) can
answer it, calls those systems through the **Model Context Protocol (MCP)**, and returns a
synthesized answer plus a full trace of which tools were invoked.

```
User question --> POST /api/query --> LLM (tool-use) --> MCP tool call(s) --> Answer
```

Three example MCP servers ship with the solution, each wrapping a mocked internal API:

| MCP server                  | Wraps (mocked)              | Tools                                                              |
|------------------------------|------------------------------|---------------------------------------------------------------------|
| `PayeeMcpServer`              | Payee master-data API        | `get_payee_by_id`, `search_payees_by_name`, `list_recent_payees`    |
| `BusinessSecurityMcpServer`   | Fraud / KYC / security API   | `get_security_profile`, `check_fraud_flags`, `list_high_risk_entities` |
| `ApprovalWorkflowMcpServer`   | Approval workflow API        | `get_approval_status`, `list_pending_approvals`, `get_approval_history_for_entity` |

See `docs/` for the full architecture (HLD) and technical (LLD) documents.

## Solution layout

```
AiOrchestrator.sln
src/
  AiOrchestrator.Core/            domain models, ILlmProvider / IMcpClient abstractions, the
                                   orchestration loop (OrchestrationService)
  AiOrchestrator.Llm.Claude/      ILlmProvider implementation for Anthropic's Claude API
  AiOrchestrator.Llm.Ollama/      ILlmProvider implementation for a local Ollama server - the
                                   automatic fallback when no Anthropic API key is set
  AiOrchestrator.Llm.Stub/        deterministic offline ILlmProvider for local smoke-testing
  AiOrchestrator.Mcp.Protocol/    JSON-RPC 2.0 + MCP wire-format contracts (shared client/server)
  AiOrchestrator.Mcp/             MCP client: stdio transport + process lifecycle management
  McpServers/
    AiOrchestrator.McpServers.Common/   reusable MCP server-side host/tool-registry
    PayeeMcpServer/                     example MCP server #1
    BusinessSecurityMcpServer/          example MCP server #2
    ApprovalWorkflowMcpServer/          example MCP server #3
  AiOrchestrator.Api/             ASP.NET Core minimal API - the single public entry point
tests/
  AiOrchestrator.Tests/           unit/integration-style tests (see "Testing" below)
docs/                             architecture (HLD) and technical (LLD) documents
```

## Prerequisites

- .NET 8 SDK
- No other tools, containers, or accounts are required to run the solution locally with the
  built-in stub LLM provider. A Claude API key is only needed once you switch to real reasoning
  (see below).

> **Almost no external NuGet packages.** Every project builds from the .NET SDK and ASP.NET Core
> shared framework alone (via `FrameworkReference`), with zero `PackageReference` entries, except
> `AiOrchestrator.Api`, which references `Swashbuckle.AspNetCore` for the `/swagger` UI (see
> below). This was originally a constraint of the sandbox this was built in (no NuGet access), and
> is otherwise a deliberate design choice: a smaller supply-chain surface and a build that mostly
> can't fail because a package registry is down. See `docs/02-technical-design.docx` for what that
> means for the MCP and JSON handling code, and how to reintroduce more packages (e.g. the
> official MCP SDK, xUnit) if your environment allows it.

## Quick start (no API key required)

This runs the full pipeline - API, all three MCP server child processes, real MCP JSON-RPC
handshakes - using the built-in deterministic **stub** LLM provider instead of a real model, so
you can see the end-to-end mechanics work with zero setup.

```bash
# 1. Build everything
dotnet build

# 2. Run the API (must be run from its own project directory, or with --project, so it
#    finds appsettings.json - see "Troubleshooting" below)
cd src/AiOrchestrator.Api
Llm__Provider=Stub dotnet run
```

Then open [http://localhost:5080/swagger](http://localhost:5080/swagger) in a browser for an
interactive Swagger UI over all three endpoints - or, in another terminal:

```bash
curl -s http://localhost:5080/health

curl -s http://localhost:5080/api/tools | python3 -m json.tool

curl -s -X POST http://localhost:5080/api/query \
  -H "Content-Type: application/json" \
  -d '{"query":"Is payee PAY-1001 currently active?"}' | python3 -m json.tool
```

Try also: `"What is the fraud risk profile for PAY-1005?"` and
`"What is the approval status of APR-5002?"` - each is routed to a different MCP server.
(Sample IDs: `PAY-1001`..`PAY-1008`, `APR-5001`..`APR-5006` - see the `Mock*ApiClient` classes.)

## Running with real Claude reasoning

```bash
export ANTHROPIC_API_KEY=sk-ant-...
cd src/AiOrchestrator.Api
dotnet run   # Llm:Provider defaults to "Claude" in appsettings.json
```

Now `/api/query` accepts genuinely open-ended English questions - the model decides for itself
which tool(s), if any, to call, in what order, and how to phrase the final answer.

Configuration lives in `src/AiOrchestrator.Api/appsettings.json` under `Llm:Claude` (model id,
timeout, etc.) - the API key itself is never put in a config file, only the *name* of the
environment variable that holds it (`Llm:Claude:ApiKeyEnvironmentVariable`, default
`ANTHROPIC_API_KEY`).

## Automatic fallback to a local Ollama model (no API key, Development only)

If `Llm:Provider` resolves to `Claude` (its default) but `ANTHROPIC_API_KEY` is not set, the API
does **not** fail every request with HTTP 500 - **in the Development environment** it logs a warning
at startup and transparently switches to `AiOrchestrator.Llm.Ollama`, which talks to a locally-running
[Ollama](https://ollama.com) server instead. This gives genuine tool-calling reasoning (unlike the
keyword-based `Stub` provider) with no API key and no cost:

```bash
ollama pull llama3.1   # any tool-calling-capable model; see https://ollama.com/search?c=tools
ollama serve           # if not already running as a background service

cd src/AiOrchestrator.Api
dotnet run              # no ANTHROPIC_API_KEY set -> auto-falls-back to Ollama
```

In any other environment the fallback is deliberately **not** applied: a missing key would quietly
change both the model and where your data goes, so startup fails with a clear message instead. Set
the key, or choose `Llm__Provider` explicitly.

Configuration lives under `Llm:Ollama` in `appsettings.json` (`BaseUrl`, default
`http://localhost:11434/`; `Model`, default `llama3.1`; `TimeoutSeconds`, default `120` - local
inference, especially on CPU or on first load, can be much slower than a hosted API; `NumCtx`, the
context window, worth raising because Ollama's own default is small enough to truncate the system
prompt plus the tool catalog; `KeepAlive`, how long Ollama keeps the model loaded between requests).
Override the model without touching the file via `Llm__Ollama__Model=<name> dotnet run`.

To select Ollama explicitly (instead of relying on the fallback) or to force real Claude even
without detecting a key at startup, set `Llm__Provider` explicitly: `Ollama`, `Claude`, or `Stub`.

## Testing

No external test framework is referenced (same no-NuGet constraint as above), so
`tests/AiOrchestrator.Tests` is a small console app with its own `[Fact]`/`Assert` runner:

```bash
dotnet run --project tests/AiOrchestrator.Tests
```

It exercises the JSON-RPC codec, the tool-name encode/decode scheme, and the orchestration loop
itself (direct answers, tool-use round trips, iteration-budget exhaustion, unknown-tool handling)
against fake `ILlmProvider`/`IMcpClient` implementations - no network, no child processes,
fully deterministic.

## Troubleshooting

**`/api/tools` returns an empty list / logs say "Starting 0 configured MCP server(s)".**
The API's configuration (`appsettings.json`) is loaded relative to its *content root*, which
ASP.NET Core defaults to the current working directory the process was started from - not the
directory the DLL lives in. Run `dotnet run` (or `dotnet <path-to-dll>`) from inside
`src/AiOrchestrator.Api`, or pass `--project src/AiOrchestrator.Api` to `dotnet run` from the
repo root.

**A query against the Claude provider returns HTTP 500 with "Environment variable
'ANTHROPIC_API_KEY' is not set".** This only happens if you forced `Llm__Provider=Claude`
explicitly without a key - by default, a missing key auto-falls-back to Ollama instead (see
above). Set the key, drop the explicit override, or switch `Llm__Provider=Stub` for offline
testing.

**A query against the Ollama provider hangs for a long time, or fails with "The request was
canceled due to the configured HttpClient.Timeout of 120 seconds elapsing."** Local inference
speed depends entirely on your hardware and the model size - a large model on a CPU-only machine
can genuinely take minutes. Pull a smaller tool-calling model (e.g. `llama3.2` or `qwen2.5:7b`),
run `ollama ps` to confirm the model is loaded, or raise `Llm:Ollama:TimeoutSeconds`.

**A query against the Ollama provider fails with "Could not reach the Ollama server... Is it
running?".** Start it with `ollama serve` (or check it's already running as a background service),
and confirm `Llm:Ollama:BaseUrl` matches where it's listening (default `http://localhost:11434/`).

## Adding a new internal system as an MCP server

1. Copy one of the `McpServers/*` projects as a template.
2. Replace its `Mock*ApiClient` with a real HTTP client for the internal API (auth, retries,
   etc. live here - this is the intended replacement seam).
3. Register `McpToolRegistration`s (name, description, JSON-Schema input, handler) in `Program.cs`
   via `AiOrchestrator.McpServers.Common.McpServerHost` - no other code changes needed.
4. Add the project to `AiOrchestrator.sln` and the built DLL path to `Mcp:Servers` in
   `src/AiOrchestrator.Api/appsettings.json`.
5. Restart the API - the new server's tools are discovered automatically at startup and
   immediately available to the orchestration loop.

## Adding a new LLM provider

Implement `AiOrchestrator.Core.Abstractions.ILlmProvider` in a new project (mirror
`AiOrchestrator.Llm.Claude` or the more recently added `AiOrchestrator.Llm.Ollama`), then add a
`case` for it in the `Llm:Provider` switch in `src/AiOrchestrator.Api/Program.cs`. The
orchestration loop itself never changes.
