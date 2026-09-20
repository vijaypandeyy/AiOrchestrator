# CODECONTEXT — AI Orchestrator explained for newcomers

This document explains **what this codebase does, how it is organised, which technologies it uses, and
how to rebuild it yourself from scratch**. It assumes you know basic C# but nothing about LLM tool use
or the Model Context Protocol (MCP).

> Companion docs: [README.md](README.md) (how to run it) and `docs/` (formal HLD/LLD Word documents).
> This file is the "explain it to me like I'm new" version.

---

## 1. The 30-second summary

You send a plain-English question to a web API:

```
POST /api/query   { "query": "Is payee PAY-1001 currently active?" }
```

The API:

1. Runs cheap safety checks on the question (**guardrails**).
2. Asks an **LLM** (Claude, or a local Ollama model, or a fake "Stub") *"which of these tools would help?"*
3. Calls the chosen tool(s) on **MCP servers** (small helper programs that wrap internal systems).
4. Hands the tool results back to the LLM, which writes the final answer.
5. Runs safety checks on the answer, then returns it **plus a trace** of every tool that was called.

```
        ┌────────┐   question    ┌───────────────┐   prompt + tool list   ┌─────────┐
 User ─▶│  API   │──────────────▶│ Orchestration │───────────────────────▶│   LLM   │
        └────────┘               │    Service    │◀───────────────────────│ (Claude/│
             ▲                   └──────┬────────┘   "call tool X(args)"  │ Ollama) │
             │  answer + trace          │                                  └─────────┘
             │                          │ tools/call (JSON-RPC over stdin/stdout)
             │                          ▼
             │            ┌──────────────────────────────┐
             └────────────│ MCP servers (child processes)│
                          │ payee | business-security |  │
                          │ approval-workflow            │
                          └──────────────────────────────┘
```

The three MCP servers wrap **mocked** in-memory data (fake payees, fake fraud profiles, fake approval
requests). In a real company you would replace the mocks with calls to real internal APIs.

---

## 2. Key concepts (read this first if any term is new)

| Term | Plain-English meaning | Where it shows up here |
|------|----------------------|------------------------|
| **LLM** | A language model that reads text and writes text. | Claude API, Ollama, Stub |
| **Tool use / function calling** | You give the LLM a menu of functions (name + description + JSON-Schema of inputs). Instead of answering, it may reply "please call function X with these args". *Your code* runs the function and sends the result back. The LLM never runs anything itself. | `LlmContentBlock` types in [LlmModels.cs](src/AiOrchestrator.Core/Models/LlmModels.cs) |
| **ReAct loop** | Think → call tool → observe result → think again, until done. | [OrchestrationService.cs](src/AiOrchestrator.Core/Orchestration/OrchestrationService.cs) |
| **MCP (Model Context Protocol)** | An open standard for how an AI app discovers and calls tools exposed by separate programs ("MCP servers"). | `AiOrchestrator.Mcp*` projects |
| **JSON-RPC 2.0** | A tiny request/response message format in JSON (`{"jsonrpc":"2.0","id":1,"method":"tools/list"}`). MCP is built on it. | [JsonRpcMessage.cs](src/AiOrchestrator.Mcp.Protocol/JsonRpcMessage.cs) |
| **stdio transport** | The API starts each MCP server as a child process and talks to it by writing one JSON line to its *stdin* and reading one JSON line from its *stdout*. | [StdioMcpTransport.cs](src/AiOrchestrator.Mcp/StdioMcpTransport.cs) |
| **Guardrails** | Plain C# checks (not the LLM) that refuse off-topic, oversized or injection-style questions. | [QueryGuard.cs](src/AiOrchestrator.Core/Orchestration/QueryGuard.cs) |
| **Dependency Injection (DI)** | ASP.NET Core hands each class the objects it needs via constructor parameters, based on registrations in `Program.cs`. | Everywhere; see `ServiceCollectionExtensions.cs` files |
| **Options pattern** | Config sections in `appsettings.json` are bound to strongly-typed classes (`IOptions<T>`). | `Core/Options/*.cs` |

---

## 3. Technology stack and libraries

| Item | Choice | Why |
|------|--------|-----|
| Language / runtime | **C# 12, .NET 8** (`global.json` pins SDK 8.0.100, rolls forward to newer feature bands) | Modern, fast, first-class ASP.NET Core |
| Web framework | **ASP.NET Core Minimal API** | Three endpoints don't need controllers |
| API docs | **Swashbuckle.AspNetCore 10.2.3** (Swagger UI at `/swagger`) | *The only NuGet package in the whole solution* |
| JSON | `System.Text.Json` (built in) | No Newtonsoft needed |
| HTTP to LLMs | `HttpClient` + `IHttpClientFactory` (built in) | Hand-written calls to Claude / Ollama REST APIs — no vendor SDK |
| DI / config / logging | `Microsoft.Extensions.*` (ship inside the ASP.NET Core shared framework) | Pulled in with `<FrameworkReference Include="Microsoft.AspNetCore.App" />` |
| Process management | `System.Diagnostics.Process` | Launches MCP servers as children |
| Tests | **Home-made mini test runner** (`[Fact]` + `Assert`) in a console app | No xUnit/NUnit dependency |
| LLM backends | Anthropic **Claude** Messages API · **Ollama** local server · **Stub** offline fake | Swappable via one config value |

**Design choice worth noticing:** the project deliberately avoids NuGet packages (including the official
MCP SDK) to keep the supply chain small and the build offline-friendly. The MCP protocol is
*hand-implemented* — only the small subset needed for tools: `initialize`, `notifications/initialized`,
`tools/list`, `tools/call`, `ping`. Note that `ping` is implemented on the **server** side only; the client
never sends one, so there is no liveness probe of a running server (health is inferred from whether the
child process is still alive). If you replicate this, you may prefer to use the official MCP SDK and
xUnit instead.

---

## 4. Solution layout and what each project does

```
AiOrchestrator.sln
global.json                       pins the .NET SDK version
src/
  AiOrchestrator.Core/            THE BRAIN — models, interfaces, orchestration loop, guardrails
  AiOrchestrator.Llm.Claude/      talks to Anthropic's API        (implements ILlmProvider)
  AiOrchestrator.Llm.Ollama/      talks to a local Ollama server  (implements ILlmProvider)
  AiOrchestrator.Llm.Stub/        keyword-based fake LLM for offline testing (implements ILlmProvider)
  AiOrchestrator.Mcp.Protocol/    JSON-RPC + MCP message shapes, shared by client AND servers
  AiOrchestrator.Mcp/             MCP CLIENT: launches server processes, sends/receives messages
  McpServers/
    AiOrchestrator.McpServers.Common/   reusable MCP SERVER runtime (McpServerHost)
    PayeeMcpServer/                     exe: 3 payee tools + mock data
    BusinessSecurityMcpServer/          exe: 3 risk/fraud tools + mock data
    ApprovalWorkflowMcpServer/          exe: 3 approval tools + mock data
  AiOrchestrator.Api/             THE ENTRY POINT — ASP.NET Core app, wires everything together
tests/
  AiOrchestrator.Tests/           console-app test runner + fakes
docs/                             HLD + technical design (.docx)
```

### Project dependency graph (arrows = "references")

```
                          ┌──────────────────┐
                          │ AiOrchestrator.Api│ (executable web app)
                          └─┬───┬───┬───┬────┘
             ┌──────────────┘   │   │   └──────────────┐
             ▼                  ▼   ▼                  ▼
      Llm.Claude / Llm.Ollama / Llm.Stub            AiOrchestrator.Mcp
             │                                     │            │
             └───────────────┬─────────────────────┘            ▼
                             ▼                          Mcp.Protocol  ◀── McpServers.Common
                     AiOrchestrator.Core                                        ▲
                     (no project refs)                                          │
                                                          Payee / BusinessSecurity / ApprovalWorkflow servers
```

Key rule: **`Core` depends on nothing else in the solution.** Everything else depends on `Core`'s
interfaces. That is what makes vendors swappable. (`Mcp.Protocol` is likewise dependency-free so both the
client and the servers can share it.)

---

## 5. The core abstractions (the "seams")

All in `src/AiOrchestrator.Core`.

### 5.1 `ILlmProvider` — "any LLM"
[Abstractions/ILlmProvider.cs](src/AiOrchestrator.Core/Abstractions/ILlmProvider.cs)

```csharp
public interface ILlmProvider
{
    string ProviderId { get; }
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}
```

The orchestration loop only ever calls `CompleteAsync`. Each provider translates between the
vendor-neutral types below and its own JSON wire format.

### 5.2 Vendor-neutral LLM messages
[Models/LlmModels.cs](src/AiOrchestrator.Core/Models/LlmModels.cs)

- `LlmMessage(Role, Content)` — a chat turn. `Content` is a list of **blocks**:
  - `TextBlock` — normal text
  - `ToolUseBlock(Id, Name, Input)` — the model says "call this tool with these args"
  - `ToolResultBlock(ToolUseId, Content, IsError)` — we report the tool's output back
- `ToolDefinition(Name, Description, InputSchema, ServerId)` — one entry on the "menu" given to the model
- `LlmRequest` — system prompt + message history + tools (+ `MaxTokens` 1536, `Temperature` 0.2)
- `LlmResponse` — content blocks + a normalised `LlmStopReason` (`EndTurn`, `ToolUse`, `MaxTokens`, `Other`)

The shape mimics Claude's content-block design because it is general enough to also cover OpenAI-style
function calling.

### 5.3 `IMcpClient`, `IMcpClientFactory`, `IToolCatalog`
[Abstractions/IMcpClient.cs](src/AiOrchestrator.Core/Abstractions/IMcpClient.cs)

- `IMcpClient` — a handle to **one** running MCP server: `InitializeAsync`, `ListToolsAsync`,
  `CallToolAsync(toolName, argumentsJson)`, `IsHealthy`.
- `IMcpClientFactory` — owns *all* servers' lifecycle (start at boot, stop at shutdown, look up by id).
- `IToolCatalog` — merges every server's tool list into one flat, cached list.

### 5.4 Data shapes for the API
[Models/OrchestrationModels.cs](src/AiOrchestrator.Core/Models/OrchestrationModels.cs)

- `QueryRequest(Query, SessionId?)` — request body
- `QueryResponse(Answer, ToolInvocations, LlmRoundTrips, SessionId, Refused, Truncated)` — response body
  (`Truncated` is true when the model hit its output-token budget, so the answer is incomplete)
- `ToolInvocationTrace(...)` — one audit row per tool call (server, tool, arguments, result, error flag, duration ms)

### 5.5 Configuration classes
[Options/](src/AiOrchestrator.Core/Options/)

| Class | appsettings section | Notable settings |
|-------|--------------------|------------------|
| `OrchestratorOptions` | `Orchestrator` | `MaxToolIterations` (6), `MaxToolResultChars` (8000), `ToolCatalogTtlSeconds` (300), `SystemPrompt`, nested `Guardrails` |
| `GuardrailOptions` | `Orchestrator:Guardrails` | `Enabled`, `MaxQueryLength` (1000), `RequireToolGrounding`, `BlockCodeInAnswers`, `BlockPromptInjectionPhrases`, `OutOfScopeMessage` |
| `ClaudeOptions` | `Llm:Claude` | `BaseUrl`, `Model`, `AnthropicVersion`, `ApiKeyEnvironmentVariable`, `TimeoutSeconds` |
| `OllamaOptions` | `Llm:Ollama` | `BaseUrl`, `Model`, `TimeoutSeconds`, `NumGpu`, `NumCtx`, `KeepAlive` |
| `McpOptions` | `Mcp` | `Servers[]`, `StartupTimeoutSeconds` (15), `CallTimeoutSeconds` (30) |

Note the API key is **never** in a config file: `ApiKeyEnvironmentVariable` holds only the *name* of the
environment variable (default `ANTHROPIC_API_KEY`).

---

## 6. The orchestration loop (the most important file)

[Orchestration/OrchestrationService.cs](src/AiOrchestrator.Core/Orchestration/OrchestrationService.cs) → `HandleQueryAsync`

```
HandleQueryAsync(request)
 │
 ├─ 1. validate non-empty; make/keep a sessionId
 ├─ 2. QueryGuard.CheckQuery(...)          → if refused: return immediately (0 LLM calls, 0 tokens spent)
 ├─ 3. tools = ToolCatalog.GetToolsAsync() → rename each to "serverId__toolName" (ToolNameCodec.Encode)
 ├─ 4. messages = [ user: "<the question>" ]
 │
 └─ 5. repeat up to MaxToolIterations times:
        a. response = llm.CompleteAsync(system prompt, messages, tools)
        b. if response is NOT "tool use":
               QueryGuard.CheckAnswer(...)            → may replace answer with refusal message
               stop reason MaxTokens → append a "this was cut off" notice, Truncated = true
               stop reason Other with no text → "the model produced no answer" message
               return QueryResponse(answer, traces)   ← normal exit
        c. append the assistant's message (incl. its tool requests) to history
        d. for each ToolUseBlock:
               InvokeToolAsync → decode name → find MCP client → CallToolAsync → ToolInvocationTrace
        e. append a user message holding one ToolResultBlock per call, each capped at
           MaxToolResultChars (the full result stays in the trace)
        (loop back to a — the LLM now sees the results)
    
    If the loop runs out → return a polite "couldn't finish, please narrow the question" answer.
```

Things a newcomer should notice:

- **Errors become data, not exceptions.** If a tool is unknown, its server is down, or it throws,
  `InvokeToolAsync` returns a trace with `IsError = true`. That error text is sent to the LLM, which can
  then explain the failure to the user instead of the whole request crashing.
- **Bounded loop.** `MaxToolIterations` stops a confused model from looping forever / burning tokens.
- **Every answer is auditable.** `QueryResponse.ToolInvocations` lists what actually ran.
- The class only knows interfaces (`ILlmProvider`, `IToolCatalog`, `IMcpClientFactory`) — it has no idea
  whether Claude or a Stub is behind them. That is why it is unit-testable with fakes.

### Tool-name codec
[ToolNameCodec.cs](src/AiOrchestrator.Core/Orchestration/ToolNameCodec.cs)

Two MCP servers could both expose a tool called `get_status`. The LLM sees one flat menu, so tools are
renamed `"{serverId}__{toolName}"` (e.g. `payee__get_payee_by_id`) and decoded when the model asks to call
them. `__` is used because Claude restricts tool names to `[a-zA-Z0-9_-]`.

### Tool catalog
[ToolCatalog.cs](src/AiOrchestrator.Core/Orchestration/ToolCatalog.cs)

Asks each *healthy* MCP client for its tools, merges them, and **caches** the result for
`Orchestrator:ToolCatalogTtlSeconds` (default 300; 0 or less rebuilds on every query). A `SemaphoreSlim`
makes refreshes thread-safe. Each client is probed and listed inside its own `try`/`catch`, so one broken
server costs its own tools and nothing else.

An **empty** aggregate is never cached: with no tools the model cannot ground an answer, and the grounding
guardrail would then refuse every query for as long as the empty list survived. The TTL is also what lets a
server that was down at startup, or that crashed and was restarted, rejoin the catalog without restarting
the API.

### Guardrails (defence in depth)
[QueryGuard.cs](src/AiOrchestrator.Core/Orchestration/QueryGuard.cs)

The system prompt *asks* the LLM to stay on topic, but a prompt is a request, not a guarantee. So there
are three layers:

| Layer | Where | What it does |
|-------|-------|--------------|
| 1. System prompt | `OrchestratorOptions.SystemPrompt` | Soft: tells the model to only answer payee / security / approval questions, only from tool results |
| 2. Input check | `QueryGuard.CheckQuery` (before the LLM) | Rejects too-long queries, code fences (```` ``` ````), and prompt-injection phrases via regex ("ignore previous instructions", "you are now", "jailbreak", …) |
| 3. Output check | `QueryGuard.CheckAnswer` (after the LLM) | Rejects answers with **no tool call behind them** (`RequireToolGrounding`) or containing code fences |

When a guardrail trips, the caller gets `OutOfScopeMessage` and `Refused = true`. These are best-effort
heuristics, not a defence against a determined attacker — the code comments say so honestly.

---

## 7. LLM providers

Each provider is its own small project that implements `ILlmProvider` and contains **all** knowledge of
that vendor's wire format. Each also ships a `ServiceCollectionExtensions.AddXxxLlmProvider(...)` method
that registers it in DI.

### 7.1 Claude — `AiOrchestrator.Llm.Claude`
[ClaudeLlmProvider.cs](src/AiOrchestrator.Llm.Claude/ClaudeLlmProvider.cs)

1. Reads the API key from the env var named in config (throws a clear error if missing).
2. Converts `LlmRequest` → `ClaudeRequestDto` (DTOs live in `ClaudeWireModels.cs`).
3. `POST {BaseUrl}messages` with headers `x-api-key` and `anthropic-version`.
4. Non-2xx → throws `ClaudeApiException` (with the vendor's error message extracted).
5. Converts the response back to `LlmResponse`; maps `stop_reason` (`tool_use` → `ToolUse`, `end_turn` → `EndTurn`, …).

### 7.2 Ollama — `AiOrchestrator.Llm.Ollama`
[OllamaLlmProvider.cs](src/AiOrchestrator.Llm.Ollama/OllamaLlmProvider.cs)

Talks to `POST api/chat` on a local Ollama server (default `http://localhost:11434/`, model `llama3.2` in
appsettings). Differences from Claude that the code has to bridge:

- The system prompt is a normal message with role `"system"`.
- Tool results are **separate `"tool"`-role messages**, not blocks inside a user message — so one
  `LlmMessage` containing several `ToolResultBlock`s expands into several wire messages.
- Ollama tool calls have **no id**, so the provider invents one (`ollama-<guid>`) just so the rest of the
  system has something to correlate on.
- Stop reason is inferred: any tool calls ⇒ `ToolUse`; `done_reason == "length"` ⇒ `MaxTokens`; else `EndTurn`.
- `LlmRequest.MaxTokens` is forwarded as `num_predict` in the request options (alongside `temperature` and
  `num_gpu`), the Ollama equivalent of Claude's `max_tokens`.
- `NumGpu: 0` in appsettings forces CPU-only inference (avoids GPU-driver crashes on old hardware).
- Connection failures are wrapped in `OllamaApiException` with a hint that Ollama may not be running.

### 7.3 Stub — `AiOrchestrator.Llm.Stub`
[StubLlmProvider.cs](src/AiOrchestrator.Llm.Stub/StubLlmProvider.cs)

A fake "LLM" for smoke-testing with no network at all. Round 1: keyword rules pick a tool
(`risk/fraud/kyc` → business-security, `approv/pending` → approval-workflow, …) and a regex pulls
`PAY-####` / `APR-####` ids out of the question. Round 2 (when tool results are present): it just echoes
the results as the "answer". Never use in production.

### 7.4 Which provider is used? (in `Program.cs`)
[Api/Program.cs](src/AiOrchestrator.Api/Program.cs)

```
Llm:Provider (default "Claude")
   │
   ├─ "Claude" and ANTHROPIC_API_KEY env var is empty ─▶ log "[startup] ... falling back" ─▶ use Ollama
   ├─ "Claude"  ─▶ AddClaudeLlmProvider
   ├─ "Ollama"  ─▶ AddOllamaLlmProvider
   ├─ "Stub"    ─▶ AddStubLlmProvider
   └─ anything else ─▶ throw with a helpful message
```

Override without editing files: `Llm__Provider=Stub dotnet run` (double underscore = `:` in env-var config).

---

## 8. MCP: how the API talks to tool servers

MCP has two halves in this repo: a **client** (inside the API process) and **servers** (separate `.exe`s).
They share the message types in `AiOrchestrator.Mcp.Protocol`.

### 8.1 Shared wire types — `AiOrchestrator.Mcp.Protocol`

- [JsonRpcMessage.cs](src/AiOrchestrator.Mcp.Protocol/JsonRpcMessage.cs) — ONE flat class used for requests,
  responses and notifications. Which fields are set tells you which kind it is:
  - request = `method` + `id`
  - notification = `method`, no `id` (never answered)
  - response = no `method`, has `result` **or** `error`, echoes `id`
- [JsonRpcCodec.cs](src/AiOrchestrator.Mcp.Protocol/JsonRpcCodec.cs) — serialises a message to **one line** of
  JSON (no indentation, nulls omitted) and back. One-line-per-message is what the MCP stdio spec requires.
- [McpMethods.cs](src/AiOrchestrator.Mcp.Protocol/McpMethods.cs) — method-name constants and protocol version `2024-11-05`.
- [McpDtos.cs](src/AiOrchestrator.Mcp.Protocol/McpDtos.cs) — `initialize`, `tools/list`, `tools/call` parameter/result shapes.

A real exchange looks like this (each line is one message):

```
→ {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"AiOrchestrator","version":"1.0.0"}}}
← {"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2024-11-05","capabilities":{"tools":{}},"serverInfo":{...}}}
→ {"jsonrpc":"2.0","method":"notifications/initialized","params":{}}
→ {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
← {"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"get_payee_by_id","description":"...","inputSchema":{...}}]}}
→ {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_payee_by_id","arguments":{"payeeId":"PAY-1001"}}}
← {"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"{...payee json...}"}],"isError":false}}
```

### 8.2 The client — `AiOrchestrator.Mcp`

Three cooperating classes, from low level to high level:

**`StdioMcpTransport`** — [file](src/AiOrchestrator.Mcp/StdioMcpTransport.cs) — *"how bytes move"*
- Starts the child `Process` with stdin/stdout/stderr redirected.
- **Write path:** `WriteLineAsync` sends a line to the child's stdin, guarded by a `SemaphoreSlim` so two
  concurrent requests can't interleave their text.
- **Read path:** a background loop (`ReadStdoutLoopAsync`) reads stdout line by line, decodes JSON-RPC,
  and finds the waiting caller by `id` in a `ConcurrentDictionary<string, TaskCompletionSource<...>>`. This
  is the classic **request/response correlation** trick: the sender registers a `TaskCompletionSource`
  under its id and `await`s it; the reader loop completes it when the matching response arrives.
  The key is the id's **raw JSON text** (`JsonRpcMessage.IdKey`), so the numeric id `7` and the string id
  `"7"` stay distinct and a server that uses string ids — which JSON-RPC 2.0 permits — correlates normally.
  Each message is decoded and dispatched inside its own `try`/`catch` (`DispatchLine`), so one unusable
  frame is logged and skipped instead of ending the loop and failing every request on that server.
- stderr is drained on another loop and logged only (stdout is reserved for protocol; stderr is for humans).
- If the process exits or stdout closes, every still-waiting request fails immediately (`FailAllPending`)
  and `IsHealthy` becomes false.
- `IsHealthy` never throws: it is false until `Start()` succeeds, and a `Process` that was never started
  (missing executable ⇒ `Process.Start()` throws `Win32Exception`) reports unhealthy rather than throwing
  `InvalidOperationException` at whoever reads it.
- `DisposeAsync` kills the process tree.

**`McpClient`** — [file](src/AiOrchestrator.Mcp/McpClient.cs) — *"what the bytes mean"*
- `InitializeAsync` = `initialize` request + `notifications/initialized` notification.
- `ListToolsAsync` = `tools/list` → `McpToolDescriptor`s.
- `CallToolAsync` = `tools/call` → concatenates the `text` content items into a string.
- `SendRequestAsync<T>` is the generic engine: increments an id (`Interlocked.Increment`), registers the
  pending task, arms a **timeout** (linked `CancellationTokenSource` → fails the task with `TimeoutException`),
  writes the line, awaits the reply, throws `McpProtocolException` on a JSON-RPC `error`, deserialises `result`.

**`McpClientFactory`** — [file](src/AiOrchestrator.Mcp/McpClientFactory.cs) — *"who's running"*
- Implements both `IMcpClientFactory` **and** `IHostedService`, so ASP.NET Core calls `StartAsync` at boot
  and `StopAsync` at shutdown automatically.
- At boot, for each enabled server in config: create client → start process → initialize → list tools.
  If a server fails, it's still registered but `IsHealthy == false`, so the API keeps running with the
  others (**graceful degradation**).
- Servers are launched **once at startup**, so individual requests never pay process-start cost.

**DI wiring** — [ServiceCollectionExtensions.cs](src/AiOrchestrator.Mcp/ServiceCollectionExtensions.cs)
registers `McpClientFactory` as a singleton and exposes the *same instance* as `IMcpClientFactory` and as
`IHostedService` (using factory lambdas). Registering it twice with `AddSingleton<T>()` would create two
different objects — a classic DI gotcha this code avoids.

### 8.3 The servers — `McpServers/*`

**`McpServerHost`** — [file](src/McpServers/AiOrchestrator.McpServers.Common/McpServerHost.cs) is a reusable mini MCP server:

```
loop: read a line from stdin
        → decode JSON-RPC
        → notification?  (log, no reply)
        → request?  switch on method:
              initialize → server info + capabilities {tools:{}}
              tools/list → every registered tool's name/description/inputSchema
              tools/call → find tool by name → run its handler → wrap text in a result
              ping       → {}
              other      → error -32601 (Method not found)
        → write one JSON line to stdout
```

Handler exceptions are caught and returned as `isError: true` results (they don't crash the server).

A tool is registered with a fluent API. Here is a trimmed version of the Payee server's
[Program.cs](src/McpServers/PayeeMcpServer/Program.cs):

```csharp
var api  = new MockPayeeApiClient();
var host = new McpServerHost("payee-mcp-server")
    .AddTool(
        name: "get_payee_by_id",
        description: "Look up a single payee by its unique payee ID ...",
        inputSchema: Schema.Parse("""
            { "type":"object",
              "properties": { "payeeId": { "type":"string", "description":"e.g. PAY-1001" } },
              "required": ["payeeId"] }
            """),
        handler: (args, _) =>
        {
            var id = args.TryGetProperty("payeeId", out var v) ? v.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) return Task.FromResult(McpToolInvocationOutcome.Failure("payeeId is required."));
            var payee = api.GetById(id);
            return Task.FromResult(payee is null
                ? McpToolInvocationOutcome.Failure($"No payee found with id '{id}'.")
                : McpToolInvocationOutcome.Ok(JsonSerializer.Serialize(payee)));
        });

await host.RunAsync();
```

**Important:** the `description` and `inputSchema` are what the *LLM* reads to decide when and how to use
the tool — write them like documentation for a smart colleague.

The `Mock*ApiClient` classes hold in-memory sample data. **This is the intended swap point** — replace a mock
with an `HttpClient` calling a real service and nothing else changes.

| Server (id) | Tools | Sample ids |
|-------------|-------|-----------|
| Payee (`payee`) | `get_payee_by_id`, `search_payees_by_name`, `list_recent_payees` | `PAY-1001`…`PAY-1008` |
| Business Security (`business-security`) | `get_security_profile`, `check_fraud_flags`, `list_high_risk_entities` | payee ids |
| Approval Workflow (`approval-workflow`) | `get_approval_status`, `list_pending_approvals`, `get_approval_history_for_entity` | `APR-5001`…`APR-5006` |

---

## 9. The API project (composition root)

[Api/Program.cs](src/AiOrchestrator.Api/Program.cs) is where everything is wired together — the only place
that knows all the concrete implementations.

1. **Logging:** single-line console logger.
2. **Register orchestrator:** bind `OrchestratorOptions`; `IOrchestrationService → OrchestrationService` (singleton).
3. **Error handling:** `AddProblemDetails()` + `AddExceptionHandler<ApiExceptionHandler>()`, activated by
   `app.UseExceptionHandler()`. Because it sits *inside* the automatic developer exception page middleware,
   the same status-code mapping applies in every environment.
4. **Swagger** registration.
5. **Pick the LLM provider** (section 7.4).
6. **MCP clients:** `AddMcpClients(configuration)`.
7. **`PostConfigure<McpOptions>`:** rewrites any *relative* `.dll` path in `Mcp:Servers[].Args` to an absolute
   path by walking up to the folder containing the `.sln` ([SolutionPathResolver.cs](src/AiOrchestrator.Api/SolutionPathResolver.cs)).
   This lets `appsettings.json` say `src/McpServers/PayeeMcpServer/bin/Debug/net8.0/PayeeMcpServer.dll`
   regardless of where you run from. It's a local-dev convenience; in production you'd publish servers and use absolute paths.
8. **Endpoints:**

| Endpoint | Purpose |
|----------|---------|
| `GET /health` | Liveness check → `{status:"healthy", timeUtc}` |
| `GET /api/tools` | Debug view of every tool discovered across all MCP servers |
| `POST /api/query` | The main entry. Success → `QueryResponse`. Failures are mapped by [ApiExceptionHandler.cs](src/AiOrchestrator.Api/ApiExceptionHandler.cs): empty query → `400`, LLM provider unreachable → `503`, throttled → `429`, upstream error → `502`, upstream timeout → `504`, anything else → `500`. All are RFC 7807 problem responses, and none carry exception text the service did not author |
| `/swagger` | Interactive docs |

### Configuration file walkthrough
[appsettings.json](src/AiOrchestrator.Api/appsettings.json)

```jsonc
{
  "Orchestrator": { "MaxToolIterations": 6, "MaxToolResultChars": 8000, "ToolCatalogTtlSeconds": 300 },
  "Llm": {
    "Provider": "Claude",                          // Claude | Ollama | Stub
    "Claude": { "BaseUrl": "https://api.anthropic.com/v1/", "Model": "claude-sonnet-4-5-20250929",
                "ApiKeyEnvironmentVariable": "ANTHROPIC_API_KEY", "TimeoutSeconds": 60 },
    "Ollama": { "BaseUrl": "http://localhost:11434/", "Model": "llama3.2", "TimeoutSeconds": 300, "NumGpu": 0,
                "NumCtx": 8192, "KeepAlive": "30m" }
  },
  "Mcp": {
    "StartupTimeoutSeconds": 15, "CallTimeoutSeconds": 30,
    "Servers": [
      { "Id": "payee", "Name": "Payee Service", "Command": "dotnet",
        "Args": ["src/McpServers/PayeeMcpServer/bin/Debug/net8.0/PayeeMcpServer.dll"], "Enabled": true },
      ... business-security, approval-workflow ...
    ]
  }
}
```

---

## 10. End-to-end walkthrough of one request

Question: **"What is the fraud risk profile for PAY-1005?"** (using Claude or Ollama)

1. **Startup (earlier):** `McpClientFactory` launched 3 child processes, handshook with each, and
   `ToolCatalog` will lazily list 9 tools total.
2. `POST /api/query` arrives → minimal-API handler checks the query isn't blank → calls `HandleQueryAsync`.
3. `QueryGuard.CheckQuery` → length OK, no code fence, no injection phrase → proceed.
4. Tools are fetched and renamed, e.g. `business-security__get_security_profile`.
5. **LLM round 1:** `CompleteAsync` gets system prompt + `[user: question]` + 9 tools. The model replies with
   a `ToolUseBlock(name="business-security__get_security_profile", input={"entityId":"PAY-1005"})`;
   stop reason `ToolUse`.
6. `InvokeToolAsync` decodes the name → `serverId="business-security"`, `tool="get_security_profile"` →
   `McpClient.CallToolAsync` → JSON line written to that child's stdin → child's `McpServerHost` runs the handler
   → reply line on stdout → `TaskCompletionSource` completes → we get `McpToolCallResult(text, isError)`.
7. A `ToolInvocationTrace` (step 1, timing, args, result) is recorded; a `ToolResultBlock` is added to history.
8. **LLM round 2:** the model sees the JSON result and writes a natural-language answer; stop reason `EndTurn`.
9. `CheckAnswer`: at least 1 tool call happened and no code fence → allowed.
10. Response: `{ answer, toolInvocations:[1 item], llmRoundTrips:2, sessionId, refused:false }`.

A question like *"What is the capital of France?"* instead ends at step 9: zero tool calls →
`RequireToolGrounding` replaces the answer with the out-of-scope message and `refused: true`.

---

## 11. Testing

[tests/AiOrchestrator.Tests](tests/AiOrchestrator.Tests) is a plain **console app**:

- `Testing/MiniTest.cs` — `[Fact]` attribute + `Assert` (`True`, `False`, `Equal`, `NotNull`, `Contains`).
- `Testing/TestRunner.cs` — finds `[Fact]` methods by reflection, runs them, prints pass/fail, exits non-zero on failure.
- `Fakes/` — `FakeLlmProvider` (returns a **scripted** list of responses and records requests),
  `FakeMcpClient`, `FakeMcpClientFactory` (in-memory, no processes).
- `Cases/` — `JsonRpcCodecTests`, `ToolNameCodecTests`, `OrchestrationServiceTests` (direct answers,
  tool round trips, iteration-budget exhaustion, unknown tools, guardrail refusals, no LLM call on rejected input).

Run: `dotnet run --project tests/AiOrchestrator.Tests`

Because `OrchestrationService` depends only on interfaces, tests script the model's replies and assert on
the trace — no network, deterministic. **This is the payoff of the interface-driven design.**

---

## 12. How to run it

```bash
# Prereq: .NET 8 SDK
dotnet build                                   # builds API AND the 3 MCP server DLLs

cd src/AiOrchestrator.Api                      # run from here so appsettings.json is found

Llm__Provider=Stub dotnet run                  # zero setup, fake LLM
# or:  export ANTHROPIC_API_KEY=sk-ant-...  && dotnet run          # real Claude
# or:  ollama pull llama3.2 && ollama serve  &&  dotnet run        # no key -> auto-fallback to Ollama
```

(PowerShell: `$env:Llm__Provider = "Stub"; dotnet run`.)

Then open <http://localhost:5080/swagger> or:

```bash
curl -s -X POST http://localhost:5080/api/query -H "Content-Type: application/json" \
     -d '{"query":"What is the approval status of APR-5002?"}'
```

**Common gotchas**

- *`/api/tools` is empty / "Starting 0 configured MCP server(s)"* → `appsettings.json` wasn't loaded, because
  the content root was not the API project. Either `cd src/AiOrchestrator.Api` first, or run
  `dotnet run --project src/AiOrchestrator.Api` from anywhere — `--project` sets the content root for you.
  The startup banner prints "Content root path:", which tells you which one you got.
- *MCP servers fail to start* → you skipped `dotnet build`; the config points at built `bin/Debug/net8.0/*.dll` files.
- *Startup fails with "ANTHROPIC_API_KEY is not set"* → outside the Development environment the Claude →
  Ollama fallback is deliberately not applied, because a missing key would silently change the model and the
  data path. Set the key, or set `Llm__Provider` explicitly.
- *`/api/query` returns 503 "Language model provider unavailable"* → the configured LLM could not be reached
  (for Ollama: is `ollama serve` running?). It is an upstream problem, which is why it is not a 500.
- *Ollama timeouts* → use a smaller model or raise `Llm:Ollama:TimeoutSeconds`.

---

## 13. Replicate it yourself — a build order that works

Build bottom-up so each step compiles and can be tested before the next.

**Step 0 — Scaffold**
```bash
mkdir AiOrchestrator && cd AiOrchestrator
dotnet new globaljson --sdk-version 8.0.100 --roll-forward latestFeature
dotnet new sln -n AiOrchestrator
dotnet new classlib -n AiOrchestrator.Core          -o src/AiOrchestrator.Core
dotnet new classlib -n AiOrchestrator.Mcp.Protocol  -o src/AiOrchestrator.Mcp.Protocol
dotnet new classlib -n AiOrchestrator.Mcp           -o src/AiOrchestrator.Mcp
dotnet new classlib -n AiOrchestrator.Llm.Stub      -o src/AiOrchestrator.Llm.Stub
dotnet new classlib -n AiOrchestrator.Llm.Claude    -o src/AiOrchestrator.Llm.Claude
dotnet new classlib -n AiOrchestrator.McpServers.Common -o src/McpServers/AiOrchestrator.McpServers.Common
dotnet new console  -n PayeeMcpServer               -o src/McpServers/PayeeMcpServer
dotnet new web      -n AiOrchestrator.Api           -o src/AiOrchestrator.Api
dotnet new console  -n AiOrchestrator.Tests         -o tests/AiOrchestrator.Tests
dotnet sln add (Get-ChildItem -Recurse *.csproj)     # PowerShell; on bash: dotnet sln add $(find . -name '*.csproj')

# references (arrow = "depends on")
dotnet add src/AiOrchestrator.Mcp reference src/AiOrchestrator.Core src/AiOrchestrator.Mcp.Protocol
dotnet add src/AiOrchestrator.Llm.Stub   reference src/AiOrchestrator.Core
dotnet add src/AiOrchestrator.Llm.Claude reference src/AiOrchestrator.Core
dotnet add src/McpServers/AiOrchestrator.McpServers.Common reference src/AiOrchestrator.Mcp.Protocol
dotnet add src/McpServers/PayeeMcpServer reference src/McpServers/AiOrchestrator.McpServers.Common
dotnet add src/AiOrchestrator.Api reference src/AiOrchestrator.Core src/AiOrchestrator.Mcp src/AiOrchestrator.Llm.Stub src/AiOrchestrator.Llm.Claude
dotnet add src/AiOrchestrator.Api package Swashbuckle.AspNetCore
dotnet add tests/AiOrchestrator.Tests reference src/AiOrchestrator.Core src/AiOrchestrator.Mcp
```

In each library `.csproj` (Core, Mcp, Llm.*, McpServers.Common) add
`<FrameworkReference Include="Microsoft.AspNetCore.App" />` — that gives you DI/Options/Logging/HttpClient
without NuGet. Set `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` everywhere.
(In a normal project you'd instead add `Microsoft.Extensions.*` packages.)

**Step 1 — `Core` models and interfaces** (no logic yet)
`LlmModels`, `McpModels`, `OrchestrationModels`, `ILlmProvider`, `IMcpClient` (+ factory, catalog),
`IOrchestrationService`, and the `Options` classes.

**Step 2 — `Mcp.Protocol`**
`JsonRpcMessage`, `JsonRpcCodec`, `McpMethods`, `McpDtos`. Write a couple of round-trip tests for the codec first.

**Step 3 — Server runtime + one server**
`McpServerHost` (read stdin lines → dispatch → write stdout) and the Payee server with a mock client.
**Test it by hand**, no client needed:
```bash
dotnet run --project src/McpServers/PayeeMcpServer
# paste this line and press Enter:
{"jsonrpc":"2.0","id":1,"method":"tools/list"}
```
You should see a JSON response on stdout. (Servers print logs to **stderr** so stdout stays clean.)

**Step 4 — MCP client**
`StdioMcpTransport` → `McpClient` → `McpClientFactory` (as `IHostedService`) → `AddMcpClients`. Then `ToolCatalog`.

**Step 5 — The stub LLM + orchestration loop**
Implement `ToolNameCodec`, `OrchestrationService` (start without guardrails), and `StubLlmProvider`.
Write tests using fakes for the LLM and MCP clients: direct answer, one tool round trip, iteration exhaustion,
unknown tool.

**Step 6 — API**
`Program.cs` with `/health`, `/api/tools`, `/api/query`, and config-driven provider selection. Run with the
Stub provider and hit it with curl/Swagger. **You now have the entire pipeline working end to end.**

**Step 7 — Real LLM provider**
Add `ClaudeLlmProvider` (DTOs → HTTP → map back). Verify the *stop-reason mapping* and the *tool-result
round-trip* — those are where provider bugs hide.

**Step 8 — Guardrails**
Add `QueryGuard` + `GuardrailOptions` and the two call sites in `HandleQueryAsync`; add tests that assert the LLM
is **not** called on refused input.

**Step 9 — More servers and providers**
Copy the Payee server for the other two domains. Add Ollama as a third provider (this is the best exercise in
"the interface really does isolate vendors").

### Extension recipes

**Add a new internal system (MCP server):** copy a `McpServers/*` project → replace the `Mock*ApiClient` with a
real `HttpClient` → register tools with `McpServerHost.AddTool(...)` → add to the `.sln` → add an entry to
`Mcp:Servers` in appsettings → rebuild + restart. Tools appear automatically; **no orchestrator code changes**.

**Add a new LLM vendor:** new class library implementing `ILlmProvider` + an `AddXxxLlmProvider` extension →
add a `case` to the `switch` in `Api/Program.cs`. The orchestration loop never changes.

---

## 14. Design principles to take away

1. **Program to interfaces at the seams that vary** (LLM vendor, MCP servers) — that made both swappable and testable.
2. **Keep vendor wire formats in one place** each; everything else uses neutral types.
3. **Errors as data inside the agent loop** so the model can react to failures.
4. **Bound every loop** (`MaxToolIterations`) and every wait (timeouts).
5. **Don't trust the prompt alone** — enforce policy in code before and after the model.
6. **Log to stderr, protocol to stdout** for stdio servers.
7. **Start long-lived helpers once** (hosted service), not per request.
8. **Degrade gracefully:** a dead MCP server or missing API key shouldn't take the whole API down.
9. **Secrets by reference:** config stores the env-var *name*, never the key.

## 15. Known limits (worth knowing before you copy it)

- MCP support is a hand-rolled subset (tools only; no resources/prompts/sampling/HTTP transport). Numeric
  and string JSON-RPC ids are both handled, and an undecodable or uncorrelatable frame is skipped rather
  than killing the connection.
- A crashed MCP server is **not** auto-restarted: its process stays dead until the API host restarts, so its
  tools are gone even though the catalog keeps retrying. The catalog itself recovers as far as it can — it
  refreshes on a TTL, never caches an empty list, and isolates each server's failure — but nothing yet
  relaunches a server process or handles the `tools/list_changed` notification (see IMPROVEMENTS.md section 5).
- `SessionId` is generated/echoed but conversation history is **not** persisted across requests — each
  `POST /api/query` is stateless, so follow-up questions ("and what is its approval status?") do not work and
  nothing can replay a past conversation.
- Token usage is **not** reported. Both providers read the counts from their API response and log them
  (`usage.input_tokens`/`output_tokens` for Claude, `prompt_eval_count`/`eval_count` for Ollama), but
  `LlmResponse` has no usage field, so the numbers never reach `QueryResponse`.
- Guardrails are regex/heuristic; the mock data is in-memory; `SolutionPathResolver` is dev-only.
- There is no authentication, authorization, rate limiting or persisted audit log, and tool results are
  treated as trusted input.

Suggested fixes for all of these are in [IMPROVEMENTS.md](IMPROVEMENTS.md) (section 1's correctness fixes are
done; sections 2-7 remain).
