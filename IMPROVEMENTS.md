# IMPROVEMENTS — roadmap from hackathon POC to a maintainable product

This document lists suggested improvements to the current codebase, grouped by theme and ordered by
priority. It is based on a read-through of the orchestrator loop, guardrails, `Program.cs`, the Ollama
provider, the MCP client/transport/factory, the tool catalog and the configuration.

**Verification status.** `dotnet build` succeeds with 0 warnings and all 38 tests pass. The section 1 items
marked **Confirmed** were reproduced against the .NET APIs involved; the rest were established by reading the
code, and each has since been exercised by a test or a live run.

**Progress.** **Section 1 is complete** (2026-09-20). Items marked **✅ Done** carry the fix and the tests that
cover it in the "Suggested fix" column. One piece is explicitly carried forward: restarting a *crashed* MCP
server process (and handling `tools/list_changed`) belongs to section 5 and is not done. Sections 2-7 are
untouched.

> Companion docs: [CODECONTEXT.md](CODECONTEXT.md) explains how the system works today;
> [README.md](README.md) explains how to run it.

**What is already good and should be kept:** interface seams at the points that vary (LLM vendor, MCP
servers), errors returned to the model as data instead of exceptions, a bounded tool loop, guardrails
enforced in code (not only in the prompt), graceful degradation when an MCP server fails, and secrets
referenced by environment-variable name.

## Priority overview

| # | Theme | Priority | Effort | Est. (h) |
|---|-------|----------|--------|---------:|
| 1 | Correctness and robustness fixes | Do before the demo | Small | ~8 (done) |
| 2 | Conversation memory, follow-ups, token accounting and streaming | High (biggest demo/product gap) | Medium | 26–36 |
| 3 | Architecture seams for extensibility | High | Medium | 31–47 |
| 4 | Security and enterprise readiness | High before any real data | Medium–Large | 45–72 |
| 5 | Operations and observability | Medium | Medium | 25–39 |
| 6 | Testing and evaluation | Medium (start early) | Small–Medium | 22–33 |
| 7 | Repo hygiene and tooling | Low, quick wins | Small | 4–7 |
| 8 | Product gaps specific to an internal chatbot | High once section 2 lands | Medium | 57–92 |

Hours are engineer-hours **with AI assistance**; the assumptions behind them, and a per-item breakdown, are
in [Effort estimates](#effort-estimates) below.

---

## 1. Correctness and robustness fixes

| Issue | Where | Why it matters | Suggested fix |
|-------|-------|----------------|---------------|
| **✅ Done — Confirmed:** `IsHealthy` throws if the child process was never started | [StdioMcpTransport.cs:33](src/AiOrchestrator.Mcp/StdioMcpTransport.cs#L33) | A missing executable makes `Process.Start()` throw `Win32Exception`, so the `if (!_process.Start())` guard never fires, and `HasExited` then throws `InvalidOperationException: No process is associated with this object`. The factory still registers the client ([McpClientFactory.cs:65](src/AiOrchestrator.Mcp/McpClientFactory.cs#L65)), and [ToolCatalog.cs:45](src/AiOrchestrator.Core/Orchestration/ToolCatalog.cs#L45) reads `IsHealthy` outside its try/catch. `/api/tools` and `/api/query` then return 500 on every call, because `_cache` stays null. | **Fixed.** `StdioMcpTransport` tracks `_started`, marks itself faulted when `Process.Start()` throws, and `IsHealthy` swallows `InvalidOperationException` from `HasExited`. `ToolCatalog.RefreshAsync` wraps the whole per-client block — health probe included — in try/catch. Tests: `StdioMcpTransportTests.IsHealthyIsFalseBeforeStart`, `.IsHealthyIsFalseWhenTheExecutableIsMissing`, `ToolCatalogTests.OneBrokenServerDoesNotFailTheWholeCatalog`. |
| **✅ Done — Confirmed:** one response with a string JSON-RPC id kills a server permanently | [StdioMcpTransport.cs:162](src/AiOrchestrator.Mcp/StdioMcpTransport.cs#L162) | `message.Id.Value.GetInt64()` throws `InvalidOperationException` on a string id, and the `catch` sits *outside* the `while` loop, so a single such frame ends the read loop, sets `_faulted` and fails every pending request. JSON-RPC 2.0 explicitly permits string ids, so any third-party MCP server can trigger this. The servers in this repo echo numeric ids, which is the only reason it is not seen today. | **Fixed.** Pending requests are keyed by `JsonRpcMessage.IdKey` (the id's raw JSON text, so `7` and `"7"` stay distinct), and message handling moved into `DispatchLine`, called inside a per-message try/catch; an uncorrelatable id is logged and skipped. Tests: `JsonRpcCodecTests.DecodesAResponseWithAStringId`, `.IdKeyKeepsNumberAndStringIdsDistinct`, `.IdKeyIsNullWhenThereIsNoUsableId`, and `StdioMcpTransportTests.SurvivesAJunkLineAndAStringIdResponseFromTheServer` against a real child process. |
| **✅ Done:** `LlmRequest.MaxTokens` is silently dropped by the Ollama provider | [OllamaLlmProvider.cs:47](src/AiOrchestrator.Llm.Ollama/OllamaLlmProvider.cs#L47) | Claude receives the budget as `max_tokens` ([ClaudeLlmProvider.cs:50](src/AiOrchestrator.Llm.Claude/ClaudeLlmProvider.cs#L50)), but the Ollama request options carry only `temperature` and `num_gpu`. The 1536-token budget is never applied, so generation is bounded only by the 300 s timeout, and the `MaxTokens` stop reason cannot fire for a limit the orchestrator believes it set. | **Fixed.** `OllamaRequestOptionsDto` gained `num_predict`, set from `request.MaxTokens`. (`NumCtx`, below, is still open and belongs in the same options object.) Test: `OllamaLlmProviderTests.SendsTheTokenBudgetAsNumPredict`. |
| **✅ Done:** caller cancellation reported as a timeout; timed-out requests never removed from `_pending` | [McpClient.cs:94-107](src/AiOrchestrator.Mcp/McpClient.cs#L94-L107) | Misleading errors and a slow leak of pending entries; a late response later logs a spurious warning. | **Fixed.** The timeout registration now completes the pending task as a cancellation or a timeout depending on which token fired, then removes it from the transport's pending map either way. Tests: `StdioMcpTransportTests.ATimedOutRequestReportsATimeoutAndLeavesTheServerUsable`, `.CallerCancellationIsReportedAsCancellationNotATimeout`. |
| **✅ Done:** non-`ToolUse` stop reasons are all treated as a final answer | [OrchestrationService.cs:82](src/AiOrchestrator.Core/Orchestration/OrchestrationService.cs#L82) | A `MaxTokens` (truncated) or `Other` response is returned to the user as if complete. | **Fixed.** A `MaxTokens` stop appends a plain "this answer was cut off" notice and sets the new `QueryResponse.Truncated` flag; an `Other` stop is logged, and an empty one returns a clear "no answer produced" message. Test: `OrchestrationServiceTests.MarksATruncatedAnswerInsteadOfReturningItAsComplete`. |
| **✅ Done:** tool results are not size-capped | [OrchestrationService.cs:107](src/AiOrchestrator.Core/Orchestration/OrchestrationService.cs#L107) | One large result can blow up token cost or overflow a small local model's context. | **Fixed.** `OrchestratorOptions.MaxToolResultChars` (default 8000) caps each result sent back to the model, marked with a visible `[truncated: N more character(s) omitted]`; the invocation trace keeps the full text. Test: `OrchestrationServiceTests.CapsAnOversizedToolResultBeforeItReachesTheModelButKeepsItWholeInTheTrace`. |
| **✅ Done:** Ollama context window not configurable | [OllamaLlmProvider.cs:47](src/AiOrchestrator.Llm.Ollama/OllamaLlmProvider.cs#L47) | Ollama's default context is small (2k–4k depending on version); the system prompt plus 9 tool schemas can be silently truncated, causing odd tool-calling behaviour. | **Fixed.** `OllamaOptions.NumCtx` and `KeepAlive` are sent as `num_ctx` (request options) and `keep_alive` (top level), and omitted entirely when unset so Ollama's own defaults still apply. `appsettings.json` ships `NumCtx: 8192`, `KeepAlive: "30m"`. Tests: `OllamaLlmProviderTests.SendsTheConfiguredContextWindowAndKeepAlive`, `.OmitsUnsetOllamaTuningOptions`. |
| **✅ Done:** API returns raw `ex.Message` and maps every failure to 500 | [Program.cs:130-134](src/AiOrchestrator.Api/Program.cs#L130-L134) | Leaks internals; an unreachable LLM looks like a server bug. The empty-query check is also duplicated in the endpoint and the service. | **Fixed.** `AddProblemDetails()` + `AddExceptionHandler<ApiExceptionHandler>()` map failures by cause: 400 (invalid request), 429 (provider throttling), 502 (provider error), 503 (provider unreachable), 504 (upstream timeout), 499 (caller hung up), 500 otherwise - and only messages this service authored are returned. Providers now throw a vendor-neutral `LlmProviderException`, so the API needs no vendor types. The empty-query check lives only in `OrchestrationService`. Verified live (400 and 503 against an unreachable model) and by `ApiExceptionHandlerTests` (6 tests). |
| **✅ Done:** Claude → Ollama fallback is silent and startup-only | [Program.cs:37-52](src/AiOrchestrator.Api/Program.cs#L37-L52) | Surprising outside local dev (a missing key quietly changes the model and data path); logged with `Console.WriteLine`, not the logger. | **Fixed.** The fallback now applies only in the Development environment and logs a warning through a real `ILogger`; anywhere else a missing key throws at startup with a message naming the variable and the environment. Verified by running the API in Production (fails fast) and Development (warns, falls back). A runtime `FallbackLlmProvider` decorator remains the option if real resilience is wanted. |
| **✅ Done (caching half):** tool catalog cache is never refreshed; crashed servers never restart | [ToolCatalog.cs](src/AiOrchestrator.Core/Orchestration/ToolCatalog.cs), [McpClientFactory.cs:62](src/AiOrchestrator.Mcp/McpClientFactory.cs#L62) | `RefreshAsync` is public and documented as the recovery hook, but nothing outside `ToolCatalog` itself ever calls it, so a server that fails at boot or dies later stays unavailable until the API restarts. Worse, a refresh that runs while every server is down caches an **empty list**, and `GetToolsAsync` only refreshes when `_cache` is null, so the orchestrator then offers the model zero tools forever and every answer is refused for lack of grounding. | **Fixed (caching).** The catalog now has a TTL (`OrchestratorOptions.ToolCatalogTtlSeconds`, default 300; 0 rebuilds every query) and never caches an empty aggregate, so a server that was down at startup rejoins on its own instead of leaving the model tool-less forever. Tests: `ToolCatalogTests.AnEmptyCatalogIsNeverCached`, `.ANonEmptyCatalogIsReusedWithinItsTtl`, `.ACatalogWithoutATtlIsRebuiltOnEveryQuery`. **Still open:** actually restarting a crashed server process and handling `tools/list_changed` - see section 5. |

---

## 2. Conversation memory, follow-ups, token accounting and streaming

The first three items below answer three questions asked of the current build directly: **no**, chat history
is not stored anywhere; **no**, follow-up questions do not work; and **no**, per-query token usage is not
reported (both providers receive the counts and only log them).

### 2.1 Session memory — nothing is stored today

`SessionId` is generated in [OrchestrationService.cs:52](src/AiOrchestrator.Core/Orchestration/OrchestrationService.cs#L52)
(or echoed back if the caller supplies one) and used only for log correlation and the response body. No
history is written anywhere - not to memory, not to a cache, not to a database - so the transcript exists
only in whatever the caller kept. A restart, or a second client, sees nothing.

- Add `ISessionStore` with `AppendAsync(sessionId, turn)` / `GetAsync(sessionId)`; an in-memory
  implementation (`IMemoryCache` with a sliding expiry) is enough to start, with Redis or a database behind
  the same interface when more than one instance runs.
- Store the *vendor-neutral* `LlmMessage` list plus each turn's `ToolInvocationTrace`s, not provider wire
  formats, so the history survives a provider switch.
- Decide the retention rule up front (turn count, age, or both) and write it down: this history contains
  payee, KYC and fraud data, so "keep everything forever" is a data-protection decision, not a default.
- Expose it: `GET /api/sessions/{id}` to replay a conversation, and `DELETE /api/sessions/{id}` so a user can
  clear their own history.

### 2.2 Follow-up questions — not supported today

Each `POST /api/query` builds its message list from the single incoming query
([OrchestrationService.cs:70](src/AiOrchestrator.Core/Orchestration/OrchestrationService.cs#L70)), so
"and what is its approval status?" has no antecedent for "its" and the model either guesses or asks for an
ID again. Sending the same `SessionId` twice changes nothing today, because nothing reads it back.

- Seed the message list from the session store, then append the new user turn. This is the whole feature once
  2.1 exists.
- Trim or summarise older turns to stay inside the context window and the token budget (`num_ctx` for Ollama
  is the binding limit for local models).
- Revisit `RequireToolGrounding` for follow-up turns: as written it refuses any turn that called no tool, so a
  clarifying question ("which payee do you mean?") is also refused. Options: count tool calls across the whole
  session, or classify clarifying questions explicitly and allow them.
- Note the security consequence: history is untrusted input on every later turn, so a prompt injection landing
  in turn 1 persists into turn 5. Re-run the input guard over restored history, or store only sanitised turns.

### 2.3 Token usage per query — measured but discarded

Both providers already receive the numbers and only log them: Claude reads `usage.input_tokens` /
`usage.output_tokens` ([ClaudeLlmProvider.cs:96-101](src/AiOrchestrator.Llm.Claude/ClaudeLlmProvider.cs#L96-L101))
and Ollama reads `prompt_eval_count` / `eval_count`
([OllamaLlmProvider.cs:102-107](src/AiOrchestrator.Llm.Ollama/OllamaLlmProvider.cs#L102-L107)). Neither reaches
the orchestrator, because `LlmResponse` has no usage field, so `QueryResponse` cannot report anything.

- Add `LlmUsage(int InputTokens, int OutputTokens)` and an `LlmUsage? Usage` field on `LlmResponse`; map it in
  both providers (and the Stub, as zeroes).
- Sum it across the loop - one query is at least two LLM round trips whenever a tool is called, so the
  interesting number is the per-query total, not the per-call one - and return it on `QueryResponse`
  alongside `LlmRoundTrips`, e.g. `Usage(InputTokens, OutputTokens, TotalTokens, Calls)`.
- Optionally attach per-round-trip usage to the trace, so an expensive query can be attributed to the step
  that caused it (usually one oversized tool result - see `MaxToolResultChars`).
- Cost is a presentation concern: keep price-per-1M-tokens in configuration per model rather than hard-coding
  it, and report a cost estimate only where a price is configured (a local Ollama model has none).
- Once the number exists, emit it as a metric too (see section 5, OpenTelemetry) so spend is visible across
  queries rather than one response at a time.

### 2.4 Streaming progress

Emit orchestration events (query accepted, tool called, tool result, final answer) over Server-Sent Events or
`IAsyncEnumerable`. This makes demos far more convincing and hides long local-model latency (the Ollama
timeout is 300 s).

### 2.5 Structured responses

Add a `RefusalReason` enum (too long, injection phrase, ungrounded, code in answer) instead of only
`Refused: bool`, and link answer claims to the tool-call steps that support them. `Truncated` (added in
section 1) is the first field of this kind.

---

## 3. Architecture seams for extensibility

1. **`IMcpTransport` abstraction.** `McpClient` constructs `StdioMcpTransport` directly
   ([McpClient.cs:36](src/AiOrchestrator.Mcp/McpClient.cs#L36)). Extracting an interface allows unit tests with
   a fake transport and a future HTTP/streamable-HTTP transport for remote MCP servers.
2. **Split `OrchestrationService`.** Keep the loop there; move dispatch, per-tool timeouts, result truncation
   and error mapping into an `IToolExecutor`. Independent tool calls in one turn (currently a sequential loop,
   [OrchestrationService.cs:102-108](src/AiOrchestrator.Core/Orchestration/OrchestrationService.cs#L102-L108))
   can then run in parallel.
3. **Guardrails as a pipeline.** Replace the static `QueryGuard` with `IInputGuard` / `IOutputGuard`
   implementations registered in DI. This allows PII redaction, an LLM-based scope classifier, or per-user
   policy later. Also fix the naming smell where `BlockCodeInAnswers` doubles as the input code-fence switch
   ([QueryGuard.cs:39](src/AiOrchestrator.Core/Orchestration/QueryGuard.cs#L39)).
4. **Data-driven domain scope.** Adding a fourth domain currently means editing the system prompt, the
   `OutOfScopeMessage`, and the hard-coded wording in
   [QueryGuard.cs:35](src/AiOrchestrator.Core/Orchestration/QueryGuard.cs#L35). Give each MCP server a
   description in config (or use the MCP `initialize` instructions), generate the prompt and refusal text from
   them, and keep the prompt in a versioned file rather than a C# string.
5. **LLM provider decorators.** Wrap `ILlmProvider` for retry with backoff on 429/5xx, token and cost metering,
   and logging. The `LlmUsage` field this needs is specified in section 2.3; a metering decorator is the natural
   place to aggregate it once the providers report it.
6. **Keyed DI for providers.** Replace the growing `switch` in `Program.cs` with .NET 8 keyed services, so
   different models can serve different roles (cheap router model, stronger answer model).
7. **Tool routing as tools grow.** Nine tools is fine. At 30+, sending every tool schema each turn hurts small
   models and cost. Plan a domain-routing step or a per-request tool subset.
8. **Project layering.** Libraries reference the whole `Microsoft.AspNetCore.App` framework, and `McpOptions`
   lives in Core although it is MCP-specific. Prefer `Microsoft.Extensions.*` abstractions in Core and move
   `McpOptions` to the MCP project. Split [OrchestratorOptions.cs](src/AiOrchestrator.Core/Options/OrchestratorOptions.cs),
   which currently holds three option classes.
9. **Revisit the "no NuGet packages" rule after the demo.** It keeps the supply chain small and is reasonable
   for a POC, but the hand-rolled provider and MCP layers will cost maintenance. Candidates:
   Microsoft.Extensions.AI (`IChatClient` with function invocation) and the official MCP C# SDK. Keeping your
   own `ILlmProvider` interface for now preserves the option to adopt either later.

---

## 4. Security and enterprise readiness

- **4.1 Authentication and authorization.** There is none. Tools run with the service identity. Introduce a user
  principal and pass it through to the MCP servers so data access is decided per user (for example, who may see
  a payee's fraud flags).
- **4.2 Tool results are untrusted input.** Backend data can carry injected instructions, and the guards only
  inspect the user's query. Delimit tool results clearly in the prompt and record the residual risk in the design
  docs.
- **4.3 Read-only vs mutating tools.** Everything is read-only today. Before any approval action exists, add tool
  classification (MCP tool annotations), per-server allowlists, and human-in-the-loop confirmation for mutating
  calls.
- **4.4 Data leaving the boundary.** KYC and fraud data reaching a hosted LLM raises PII and data-residency
  questions. Add redaction hooks and a config switch that restricts allowed providers (for example, local only).
- **4.5 Persistent audit log.** Traces are returned to the caller but stored nowhere. Persist who asked what, which
  tools ran, with what arguments, and what was returned.
- **4.6 API hardening.** Add rate limiting, request-size limits, CORS policy, and stop returning exception text to
  callers.
- **4.7 Guardrail limits.** The current regex checks are English-only and best-effort (the code says so). Consider a
  cheap classifier step for scope and injection detection when the stakes rise.

---

## 5. Operations and observability

- **5.1 Real health checks.** `/health` is a static "healthy". Add readiness checks for MCP server state and LLM
  reachability.
- **5.2 Self-healing MCP servers.** Restart crashed servers with backoff, and handle the `tools/list_changed`
  notification so `ToolCatalog` refreshes itself.
- **5.3 Launching servers without `bin/Debug` paths.** `SolutionPathResolver` exists because config points at
  `bin/Debug/net8.0/*.dll`. Use published output or `dotnet run --project`, and add a Dockerfile / compose file
  so the whole stack starts with one command.
- **5.4 OpenTelemetry.** Add `ActivitySource` spans (query → LLM call → tool call), a correlation ID, and metrics
  for latency, tokens, refusals and tool errors.
- **5.5 Fail-fast configuration.** Use `ValidateDataAnnotations().ValidateOnStart()` on the options classes so bad
  config stops startup instead of surfacing on the first request.

---

## 6. Testing and evaluation

- **6.1 Move to xUnit.** The home-made runner works but does not plug into `dotnet test`, IDE test explorers or CI.
  Today the suite runs only via `dotnet run --project tests/AiOrchestrator.Tests`.
- **6.2 Close the remaining coverage gaps.** Section 1 took the suite from 14 to 38 tests and closed several of
  these: the Ollama provider has wire-mapping tests over a stub `HttpMessageHandler`, `McpClient` is exercised
  against a real child process (timeouts, caller cancellation, malformed and uncorrelatable frames), and the
  API's error mapping is covered. Still missing:
  - the same wire-mapping tests for the Claude provider (stop-reason mapping and tool-result round trips are
    where provider bugs hide);
  - direct `QueryGuard` tests, rather than only its behaviour through `OrchestrationService`;
  - one end-to-end test with `WebApplicationFactory`, the Stub LLM and the real MCP servers;
  - a fake `IMcpTransport` (section 3.1) so client edge cases such as "server exits mid-request" can be
    provoked deterministically instead of with a real process.
- **6.3 Add an evaluation set (highest value for an LLM project).** A JSON file of `question → expected tool(s) /
  expected refusal`, run against the real model on demand. It catches regressions when prompts, models or tools
  change, and it is a strong story for the demo.
- **6.4 CI.** A GitHub Actions workflow that builds and runs tests on every push.

---

## 7. Repo hygiene and tooling

A `.gitignore` is in place; everything below is currently absent from the repository root.

- **7.1** `Directory.Build.props` for shared settings (nullable, `TreatWarningsAsErrors`, language version). The five
  library projects repeat the same four properties and the same `FrameworkReference` today.
- **7.2** Central package management via `Directory.Packages.props`.
- **7.3** `.editorconfig` for consistent style.
- **7.4** Keep `CODECONTEXT.md` "Known limits" (section 15) in sync as items on this list are completed.

---

## 8. Product gaps specific to an internal chatbot

Sections 1-7 treat this as a service. These are the gaps that show up once real colleagues inside the
company start using it as a *chatbot*, and they are the ones most often discovered late.

### 8.1 There is no chat surface

There is no UI in the repository (no `wwwroot`, no front end) and no messaging integration: the only clients
are `curl` and `/swagger`. Internal users will not adopt a curl endpoint, and every extra hop to reach the
tool costs adoption.

- Decide the primary surface deliberately: a small web chat page served by the API, or a Microsoft Teams /
  Slack app. For an internal enterprise tool the messaging platform usually wins, because it inherits the
  identity, the directory and the place people already are.
- Whatever the surface, keep it a client of `POST /api/query`, so the orchestrator stays headless and a
  second surface costs nothing architecturally.
- Add `GET /api/capabilities`, generated from the tool catalog and the per-server descriptions proposed in
  section 3.4, so the surface can render "here is what I can answer" plus a few sample questions instead of
  an empty text box. A chatbot that cannot say what it knows gets abandoned after two failed questions.

### 8.2 Nothing captures whether an answer was any good

Every answer is returned and forgotten. There is no thumbs up/down, no comment, and no record of a bad
answer.

- Add `POST /api/feedback` keyed on `SessionId` plus the answer, storing a rating and optional free text next
  to the trace that produced it (needs the audit log from section 4 and the session store from 2.1).
- Real traffic is the cheapest source for the evaluation set section 6 asks for: a thumbs-down with its query
  and trace is a ready-made regression case. Promote them into the eval file deliberately, with the data
  reviewed first, since queries contain payee identifiers.
- Track refusal rate, "unknown tool requested" count and empty-catalog events as leading indicators. A rising
  refusal rate usually means the scope wording or the tool descriptions drifted, not that users got worse.

### 8.3 Users do not know internal IDs

The guardrail message asks for a payee ID or an approval ID, but people say "the ACME invoice" or "that
supplier we flagged last week". `search_payees_by_name` exists, so the capability is there; the *flow* is not.

- Adopt search-then-confirm: resolve the name, and when more than one candidate matches, ask the user to pick
  before calling anything else. That is exactly the clarifying-question turn `RequireToolGrounding` currently
  refuses (see 2.2), so the two changes belong together.
- Echo what was resolved in the answer ("ACME Ltd - PAY-1003"), so a wrong resolution is visible rather than
  silently answered.

### 8.4 One slow tool blocks every user

Each MCP server reads a line, awaits the handler, then reads the next
([McpServerHost.cs:57-70](src/McpServers/AiOrchestrator.McpServers.Common/McpServerHost.cs#L57-L70)): one
in-flight request per server process, per API instance. The orchestrator also dispatches a turn's tool calls
sequentially (section 3.2). With mocked in-memory data nobody notices; behind a real backend that takes two
seconds, ten concurrent users queue.

- Make the server host dispatch each request without blocking its read loop (start the handler, write the
  response when it completes - the protocol already correlates by id), or run a small pool of processes per
  server behind the client.
- Give each server a concurrency limit and a queue-depth metric, so a slow domain degrades itself instead of
  stalling the others (a bulkhead).
- Decide the scaling model before deployment: every API instance starts its own child processes, and an
  in-memory session store means requests must stick to the instance holding the conversation.

### 8.5 Chat latency is a product requirement, not a timeout

The Ollama timeout is 300 s. Nobody waits five minutes in a chat window. Set an explicit latency budget (for
example p95 under 8 s for a single-tool answer), then design to it: a small fast model for scope
classification and routing, the stronger model only for synthesis (section 3.6's keyed DI makes this cheap),
and streaming (2.4) so the user sees progress instead of a spinner.

### 8.6 Cost and quota control per user

Once token usage exists (2.3) and users are identified (section 4), enforce budgets: per-user and per-team
daily token caps, a per-user rate limit, and a hard ceiling per query. An internal tool with no quota is one
badly-worded loop away from a surprising invoice, and a shared service account hides who caused it.

### 8.7 Traces are debugging output, not chat content

`QueryResponse.ToolInvocations` returns the full text of every tool result to the caller, including whatever
KYC or fraud detail the backend returned. That is excellent for debugging and wrong for a chat transcript
that gets screenshotted into a group channel.

- Make trace verbosity a per-role setting: full traces for operators and debugging, a summary ("checked Payee
  Service and Business Security") for ordinary users.
- Decide retention and scrubbing for logs and audit records separately from the chat history (2.1), and keep
  query text out of ordinary logs unless the audit log is the thing storing it.

### 8.8 Record what answered each query

Nothing records which system prompt version or which model produced an answer. When quality changes after a
prompt edit or a model upgrade, there is no way to tell which answers came from which configuration.

- Stamp every response and audit row with the model id and a prompt version (the versioned prompt file
  proposed in section 3.4 gives you the version).
- Gate prompt and model changes on the evaluation set (section 6), and roll them out to a subset first. A
  model upgrade is a behaviour change, and for an internal tool the regression usually surfaces as "it used
  to answer this".

### 8.9 Smaller things worth a ticket

- Move the API key from an environment variable to a managed secret store (Key Vault, Secrets Manager) with a
  rotation story; the current indirection is good, but rotation still means a redeploy.
- Cache tool results per tool and arguments with a short TTL, and label answers with an "as of" time. Fraud
  flags and KYC status are exactly the data where a stale-but-unlabelled answer is worse than a slow one.
- Guardrails and refusal messages are English-only; if the company is not, that is a correctness problem
  rather than a nicety.
- Give support staff a way to look up a session and replay what happened. Every internal tool eventually
  attracts a "why did it say that?" question, and an audit log only helps if something can read it back.

---

## Effort estimates

All figures are **engineer-hours for one developer already familiar with this codebase, working with an AI
coding assistant (Claude)**, and include writing tests and updating `CODECONTEXT.md` / `README.md`. They
exclude code review turnaround, deployment windows, and anything that waits on another team (an identity
provider, a Teams app registration, a production data source).

Assistance helps unevenly, and the ranges already account for it: roughly 2-3x on mechanical work (wire
mappings, test conversion, option plumbing), but closer to 1.2x where the cost is a design decision, an
integration with a system outside this repository, or an evaluation loop that needs a real model in the
loop. Ranges are honest uncertainty, not padding - pick the low end only when the item has no unknowns left.

**Calibration.** Section 1 - ten fixes, 24 new tests, three docs updated - is the work already completed in
this repository, and it came to roughly **8 h** of assisted effort. Every number below is scaled against
that, so if section 1 felt faster or slower than 8 h in your hands, scale the rest by the same factor.

| Item | Est. (h) | Depends on / notes |
|------|---------:|--------------------|
| **Section 2 - memory, follow-ups, tokens, streaming** | **26-36** | |
| 2.1 Session memory (`ISessionStore`, in-memory impl, retention, session endpoints) | 6-8 | Redis/database implementation adds 4-6 h |
| 2.2 Follow-up questions (seed history, trimming, grounding rule) | 4-6 | 2.1; add 4-6 h if history is *summarised* rather than trimmed |
| 2.3 Token usage (`LlmUsage`, both providers + stub, per-query totals) | 3-4 | Independent - smallest useful win in this section |
| 2.4 Streaming progress (SSE / `IAsyncEnumerable` through the loop) | 8-12 | Touches the orchestration loop and every client |
| 2.5 Structured responses (`RefusalReason`, claim-to-step links) | 2-3 | Claim linking is the larger half |
| **Section 3 - architecture seams** | **31-47** | Excludes 3.9 adoption |
| 3.1 `IMcpTransport` abstraction + fake-transport tests | 3-4 | Makes 3.2 and section 6 cheaper |
| 3.2 Split `OrchestrationService` / `IToolExecutor` (+ parallel calls) | 5-7 | |
| 3.3 Guardrails as an `IInputGuard` / `IOutputGuard` pipeline | 4-6 | Includes the `BlockCodeInAnswers` naming fix |
| 3.4 Data-driven domain scope (server descriptions, generated prompt, prompt file) | 5-7 | Unlocks 8.1's capabilities endpoint and 8.8's prompt version |
| 3.5 LLM provider decorators (retry/backoff, metering, logging) | 4-6 | 2.3 for metering |
| 3.6 Keyed DI for providers | 2-3 | Prerequisite for the router model in 8.5 |
| 3.7 Tool routing for 30+ tools | 6-10 | Needs the eval set (6.3) to prove it did not regress |
| 3.8 Project layering (framework refs, `McpOptions` move, options split) | 2-4 | Mechanical; assistant-friendly |
| 3.9 Revisit the no-NuGet rule (spike only: MS.Extensions.AI / MCP SDK) | 6-12 | Spike to a decision. Adopting either is a 40-80 h migration |
| **Section 4 - security and enterprise readiness** | **45-72** | Largest external dependency risk |
| 4.1 AuthN/AuthZ + user principal propagated to MCP servers | 12-20 | Depends on your IdP; on-behalf-of flows dominate the range |
| 4.2 Tool results treated as untrusted (delimiting, prompt, docs) | 2-3 | |
| 4.3 Read-only vs mutating tools (annotations, allowlist, human confirmation) | 8-12 | Only needed before the first mutating tool exists |
| 4.4 Data boundary (redaction hooks, allowed-provider switch) | 6-10 | Redaction quality, not plumbing, is the cost |
| 4.5 Persistent audit log (schema, store, write path, retention) | 8-12 | Storage choice; overlaps 2.1 |
| 4.6 API hardening (rate limits, request size, CORS) | 3-5 | |
| 4.7 Guardrail limits (classifier step for scope and injection) | 6-10 | Needs 6.3 to show it beats the regexes |
| **Section 5 - operations and observability** | **25-39** | |
| 5.1 Real health/readiness checks (MCP state, LLM reachability) | 3-4 | |
| 5.2 Self-healing MCP servers (restart with backoff, `tools/list_changed`) | 8-12 | The half of section 1's last row left open |
| 5.3 Publish-based server launch + Dockerfile / compose | 6-10 | |
| 5.4 OpenTelemetry spans, correlation id, metrics | 6-10 | Add collector/back-end setup separately |
| 5.5 Fail-fast options validation (`ValidateOnStart`) | 2-3 | |
| **Section 6 - testing and evaluation** | **22-33** | |
| 6.1 Move to xUnit (convert the 38 existing tests, drop MiniTest) | 4-6 | Mechanical; one of the best assistant-leveraged items |
| 6.2 Close coverage gaps (providers, `McpClient`, `WebApplicationFactory` e2e) | 8-12 | 3.1 makes the client tests much cheaper |
| 6.3 Evaluation set (format, runner, first 30-50 cases) | 8-12 | Needs a real model to run against; budget model time |
| 6.4 CI workflow (build + test on push) | 2-3 | |
| **Section 7 - repo hygiene** | **4-7** | |
| 7.1 `Directory.Build.props` | 1-2 | |
| 7.2 Central package management | 1-2 | Only matters once packages exist (3.9) |
| 7.3 `.editorconfig` | 1-2 | Expect a one-off formatting commit |
| 7.4 Keep `CODECONTEXT.md` in sync | ~1 per change | Ongoing, not a one-off |
| **Section 8 - internal chatbot product gaps** | **57-92** | Web-chat surface; a Teams app moves the top end |
| 8.1 Chat surface - minimal web chat page | 12-16 | A Teams/Slack app instead is 24-40 h (registration, bot auth) |
| 8.1 `GET /api/capabilities` | 2-3 | 3.4 |
| 8.2 Feedback capture (endpoint, storage, promotion into the eval set) | 5-8 | 2.1, 4.5, 6.3 |
| 8.3 Search-then-confirm disambiguation flow | 6-10 | 2.2 - the grounding rule must change first |
| 8.4 Concurrency (non-blocking server host, pooling, limits, metrics) | 8-14 | Do before any real backend goes behind a tool |
| 8.5 Latency budget (router model, measurement) | 6-10 | 3.6, 2.4 |
| 8.6 Per-user cost and quota controls | 6-10 | 2.3, 4.1 |
| 8.7 Per-role trace verbosity | 3-5 | 4.1 |
| 8.8 Stamp model id + prompt version; gate rollouts | 4-6 | 3.4, 6.3 |
| 8.9 Secrets store / tool-result caching / session replay for support | 12-18 | Localisation is excluded below - see note |

**Roadmap totals.** Sections 2-8 come to roughly **210-325 h** (6-9 working weeks for one engineer), and the
spread is real: it is mostly section 4's identity work and section 8's chosen surface. Two items are
deliberately outside the totals because they are projects in their own right: adopting Microsoft.Extensions.AI
or the official MCP SDK (3.9, 40-80 h) and non-English guardrails and refusals (8.9, 8-16 h per additional
language, plus native-speaker review).

**A shorter path to something demonstrably useful:** 2.3 tokens, 2.1 + 2.2 memory and follow-ups, 6.1 + 6.4
xUnit and CI, then 8.1 a minimal chat page - about **35-50 h**, and it turns the current service into
something colleagues can actually hold a conversation with.

---

## Suggested order of work

1. ~~**Section 1 fixes** — small, and they remove demo-day failure modes.~~ **Done.**
2. **Session memory, follow-up questions and token accounting + streaming** (section 2) — the largest
   visible gain. Memory (2.1) unlocks follow-ups (2.2) almost for free; token usage (2.3) is independent and
   small enough to do first.
3. **`IMcpTransport`, `IToolExecutor`, guardrail pipeline** (section 3, items 1–3) — the seams that make later
   features cheap.
4. **Evaluation set + xUnit + CI** (section 6) — start early so every later change is measured.
5. **AuthN/AuthZ, audit log, redaction** (section 4) — required before any real data is used.
6. **Operations** (section 5) — as the deployment target becomes concrete.
7. **Chatbot product gaps** (section 8) - the surface (8.1), feedback (8.2) and the disambiguation flow (8.3)
   are what turn a working service into something colleagues actually use; the rest of section 8 is best decided
   before a real deployment rather than after.
