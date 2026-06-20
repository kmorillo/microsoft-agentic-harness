# Plan: Real Token Streaming (replace the fake typewriter)

**Status:** Greenlit to draft — NOT built. Needs implementation go-ahead from Matt.
**Owner decision pending:** approve approach (ambient sink vs callback-on-command) + the go/no-go spike result.
**Origin:** Gap analysis of the "Streaming Responses from LLMs (SSE, Chunking, UX)" article, 2026-06-19. The article's central premise — show words as the model generates them so the user sees output in ~400ms instead of waiting for the whole response — is the one thing the harness does NOT do.

---

## Problem (plain language)

Today the chat UI *looks* like it streams, but it doesn't. On every message the harness:

1. Waits for the **entire** model response to finish generating (the full multi-second wait, nothing on screen but a thinking indicator).
2. **Then** chops the finished text into 50-character pieces and replays them to the browser to fake a typewriter effect.

So the user pays the full latency **and then** watches a cosmetic type-out. Perceived latency — the entire point of streaming — is not improved at all. This is the real gap; the markdown-flicker fix already shipped is cosmetic by comparison.

## Current state (verified, with evidence)

- **Fake streaming origin:** `ConversationOrchestrator.DispatchTurnAsync` calls `_mediator.Send(command, ct)` (blocking, full response) at `ConversationOrchestrator.cs:266`, then fake-streams the finished string at `:295-296` via `StreamChunksAsync` (`:380-389`, fixed 50-char chunks).
- **Blocking model call:** `ExecuteAgentTurnCommandHandler.Handle` uses `agent.RunAsync(messages, cancellationToken: ct)` at `ExecuteAgentTurnCommandHandler.cs:115` — returns the complete response, then runs all observability/usage/snapshot work after it.
- **Transport callback already exists:** the hub already passes a per-chunk callback down — `AgentTelemetryHub.cs:165-166` (`SendMessage`), `:191-192` (`RetryFromMessage`), `:220-221` (`EditAndResubmit`) → `IConversationOrchestrator.*Async(..., Func<string,CancellationToken,Task>? onChunk, ...)`. The plumbing to push chunks to the client is in place; only the *source* of the chunks is wrong.
- **Cancellation already correct:** `Context.ConnectionAborted` → orchestrator → `_mediator.Send(ct)` → `RunAsync(ct)`. A blocking call aborts its HTTP request on cancel, so a user closing the tab does NOT burn tokens. This stays true with streaming (`RunStreamingAsync` takes the same token).

## Feasibility (verified against Microsoft docs, Agent Framework RC2)

- `AIAgent.RunStreamingAsync(IEnumerable<ChatMessage> messages, AgentSession? = null, AgentRunOptions? = null, CancellationToken = default)` returns `IAsyncEnumerable<AgentResponseUpdate>`. Each update is a portion of the response; concatenating `.Text` yields the full text. Source: learn.microsoft.com `microsoft.agents.ai.aiagent.runstreamingasync` (Microsoft.Agents.AI.Abstractions v1.0.0-rc2).
- The cached agent is exactly this `AIAgent` type (`IAgentConversationCache.GetOrCreateAsync` → `Task<AIAgent>`).
- **Verify at implement time:** the exact update type name (`AgentResponseUpdate` vs `AgentRunResponseUpdate`) against the *pinned* package version in this repo — RC1→RC2 renamed several `AgentRun*`/`Agent*` types. The existing handler already uses `AgentResponse`/`ChatResponse`, so confirm the streaming sibling name there first.

## Frontend impact: essentially none (verified)

The existing SignalR wire contract already supports real streaming with no UI change for the happy path:
- `TokenReceived {token, isComplete:false}` → client appends to live `streamingContent` (`useChatStore.ts:64`).
- `TokenReceived {isComplete:true}` is **ignored** by the client (`useAgentHub.tsx:72`) — so the hub's existing final full-text token is a harmless no-op (no doubling).
- `TurnComplete {fullResponse}` → `finalizeStream` **replaces** the live buffer with the authoritative full message (`useChatStore.ts:68`).

So real per-token deltas simply replace the fake 50-char slices on the same wire. The already-shipped `completeStreamingMarkdown` fix covers incomplete code fences during real streaming too.

---

## Approach

### Recommended: ambient streaming sink (mirrors the existing `LlmUsageCapture.Current` pattern)

The only real problem is getting per-token deltas *out* of the MediatR handler (where `RunStreamingAsync` must live, alongside usage capture + observability) and back to the orchestrator's `onChunk` callback — across the request/response MediatR boundary.

Mirror the ambient pattern the handler already uses for usage capture (`ExecuteAgentTurnCommandHandler.cs:109` sets `LlmUsageCapture.Current = _usageCapture;`):

1. **New seam** `IAgentTurnStreamSink` (Application interface) with an `AsyncLocal`-backed ambient `Current`, exposing `Task EmitAsync(string delta, CancellationToken ct)` and a `bool IsActive`. Place: `Application.AI.Common/Interfaces/` + a small default impl.
2. **Orchestrator (Presentation)** sets the ambient sink to wrap the hub's `onChunk` before `_mediator.Send`, clears it in `finally`. The command record stays pure data.
3. **Handler** branches: if `IAgentTurnStreamSink.Current?.IsActive == true`, run `RunStreamingAsync`, accumulate `update.Text` into a `StringBuilder`, and `await sink.EmitAsync(delta, ct)` per non-empty delta; else keep `RunAsync` (preserves the test path and non-interactive callers). After the loop, build the same `responseText` + take the usage snapshot exactly as today, so all downstream observability/snapshot/history code is unchanged.
4. **Orchestrator** deletes `StreamChunksAsync` and the `if (onChunk is not null) await StreamChunksAsync(...)` block (`:295-296`); chunks now originate in the handler. `EmitTurnEventsAsync` stays as-is (final `TurnComplete` remains authoritative).

Why this approach: keeps the MediatR command serialization-safe and cache-key-safe, keeps **all** observability/usage logic in one place (the handler), follows a convention already proven in this exact file, and degrades gracefully to blocking when no sink is set.

### Alternative: callback on the command

Add `Func<string,CancellationToken,Task>? OnTokenDelta` to `ExecuteAgentTurnCommand`. Simpler to read, but puts a delegate on a MediatR record that implements `IContentScreenable`/`IConsumesTokens` (breaks record value-equality; risks any cache-key/behavior that inspects the command). Documented as the fallback if the ambient seam is rejected.

---

## Key risks (in priority order)

1. **GO/NO-GO SPIKE — RESOLVED 2026-06-19. Streaming path drops token/cost today, BUT the fix is bounded (not a blocker).**
   - **Confirmed broken:** `ObservabilityMiddleware.GetStreamingResponseAsync` (`ObservabilityMiddleware.cs:84-103`) records NOTHING. Its own comment (`:100-102`) says: *"ChatResponseUpdate does not expose UsageDetails — token usage is not captured for streaming calls. When accurate usage tracking is required, prefer the non-streaming GetResponseAsync path."* The blocking path (`:53-81`) records usage from `response.Usage`. A naive switch to streaming = silently zeroed tokens/cost. **Naive switch is NO-GO.**
   - **That comment is outdated.** Verified against Microsoft.Extensions.AI v10.7: streaming usage IS recoverable — it arrives as a `UsageContent : AIContent` item inside the final chunk's `Contents`. Two clean capture options inside the middleware `await foreach`: (a) detect `UsageContent` in `chunk.Contents` and `Record(...)` (same shape as the blocking block), or (b) accumulate updates and call `ChatResponseExtensions.ToChatResponse(updates)` after the loop, then read `.Usage` exactly like `GetResponseAsync` does today.
   - **Net:** the usage-capture fix is ONE method in ONE file (`ObservabilityMiddleware.GetStreamingResponseAsync`), additive, mirroring existing code — not an architecture change. In-scope for this PR, done FIRST, with a test asserting a streamed turn records the same non-zero tokens/cost as the blocking turn.
   - **Residual runtime validation (not a blocker):** streaming usage only arrives if the provider emits it. OpenAI/Azure OpenAI need a `StreamOptions.IncludeUsage`-equivalent; the harness's Azure AI Foundry Anthropic endpoint should surface usage in its stream-delta — confirm with one real streamed call that `UsageContent` is present. Fall back to `ToChatResponse().Usage` if a provider's per-chunk emission is absent. Decide per provider at implement time.
2. **Tool-call turns:** `RunStreamingAsync` interleaves tool-call activity with text updates. Confirm `update.Text` yields only assistant text deltas (so tool-call chatter isn't streamed as visible text) and that tool names still surface via the ambient `LlmUsageCapture` exactly as in the blocking path. Multi-step tool chains must still produce a coherent streamed answer.
3. **Mid-stream failure:** if the model errors after N tokens, the client holds partial text. Today `Error` clears the stream. Decide: keep current behavior (clear + generic error) or add a "response interrupted" affordance (optional follow-up, not blocking).
4. **Ordering vs `SpanReceived`/`ToolCallStarted`:** those events come from the OTel bridge on a different path; confirm real streaming doesn't reorder them relative to text in a way the UI mishandles.

## Files to touch

- `Application.AI.Common/Interfaces/IAgentTurnStreamSink.cs` (new) + default impl.
- `Application.AI.Common/DependencyInjection.cs` — register the sink.
- `Application.Core/CQRS/Agents/ExecuteAgentTurn/ExecuteAgentTurnCommandHandler.cs` — streaming branch + accumulation.
- `Presentation.AgentHub/Services/ConversationOrchestrator.cs` — set/clear ambient sink; delete `StreamChunksAsync`.
- (Conditional) the LLM usage/observability middleware — only if the spike shows the streaming path isn't instrumented.
- Tests (see below).

## Testing plan

- **Handler unit:** with an active fake sink, a stubbed agent that yields 3 updates emits 3 deltas in order and returns the concatenated text + unchanged usage snapshot. With no sink, falls back to `RunAsync` (existing tests stay green).
- **Orchestrator:** real deltas flow to `onChunk`; `StreamChunksAsync` removal verified (no 50-char artifacts); `TurnComplete.fullResponse` equals the concatenation of streamed deltas.
- **Usage capture:** a streamed turn records the same non-zero input/output tokens + cost as the equivalent blocking turn (guards risk #1 permanently).
- **Cancellation:** cancelling mid-stream stops further deltas and does not append a partial assistant message (or appends a clearly-marked partial, per the risk #3 decision).
- **Tool turn:** a turn that invokes a tool still streams a coherent final answer and records the tool.

## Out of scope (separate optional follow-ups)

- **Render pacing / typewriter queue** (article §Step 2) — smooths bursty deltas; marginal, only matters at very high token rates. Defer.
- **"Response interrupted, continue" affordance** (article §Step 6) — nice UX, not required for the core win.
- **Typed inline token events** (a `type` field on each token) — the harness already uses separate typed events; not needed.

## Already shipped (related)

- Markdown-flicker fix: `completeStreamingMarkdown.ts` + wiring in `Markdown.tsx`/`MessageItem.tsx` (9 tests). Helps both the current fake stream and future real streaming.
