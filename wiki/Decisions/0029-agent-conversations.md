# 0029. Hold agent conversations as one run per turn, with final-answer history and sticky taint

A conversation is a sequence of ordinary agent runs. Each turn is given the earlier turns' messages and final answers,
one turn runs at a time, and a turn that read untrusted content taints every turn after it.

## Status

- **Status:** Accepted
- **Date:** 2026-11-22

## Context

Every run started from nothing: `TranscriptBuilder` sent the run's own prompt and steps, and `AgentRun.SessionId`
only grouped runs (nothing read it, and nothing checked whose session a client named). A person could not ask a
follow-up ("and how much VAT is due?"). A test with a real model (2026-11-22) confirmed the runtime end to end and
left this as the main product gap.

Constraints from the earlier decisions: a run is the unit of durability, budget, quota and erasure
([ADR-0026](0026-durable-agent-runs-on-hangfire.md)); every tool call is decided in code from the initiator's current
permissions and, after untrusted content was read, by a person
([ADR-0025](0025-agentic-module-and-model-seam.md), [ADR-0027](0027-agent-approvals-triggers-and-policies.md)).

## Decision

- **One run per turn.** `AgentConversation` (tenant data, forced RLS) holds the owner, the agent, an optional title,
  the status and the message count. Each message starts a normal run through `AgentRunStarter`, linked by
  `AgentRun.ConversationId` and numbered by `AgentRun.ConversationTurn`. Agents opt in with
  `AgentDefinition.Conversational`.
- **History is final answers only.** The next turn is given each earlier finished turn as its user message and final
  answer (text, or the typed result as JSON), newest first within `Agentic:Conversations:MaxHistoryTurns` and
  `MaxHistoryCharacters`, rebuilt from the database for every model turn. Tool calls and tool results of earlier turns
  are not replayed; tools read current data again.
- **One turn at a time.** A message is refused with 409 `conversation_busy` while a turn is queued, running or waiting
  for an approval. The database enforces it under any race: a filtered unique index allows one active run per
  conversation, and turn numbers are unique per conversation.
- **Sticky taint.** A turn starts tainted when any earlier turn of its conversation read untrusted content; its changes
  then need a person. The earlier answer, which may carry injected text, is part of this turn's input.
- **Private to the owner.** Only the person (or API key) that started a conversation continues, renames or deletes
  it; `agentic.runs.read` may read it. The agent's permission and allowed triggers are checked again for every
  message.
- **Delete erases.** Deleting stops a turn in progress and erases the content of every turn; a turn still running is
  erased when it stops, and the daily purge erases any turn left over. Account erasure closes the user's conversations.
- **`SessionId` is replaced.** The column becomes `conversation_id` (existing values are cleared: they named no
  conversation) and `POST /api/v1/agent-runs` no longer accepts it. This is a breaking change for clients that sent
  `sessionId`; they were only grouping runs.

## Consequences

- Conversations reuse every guarantee of a run with no new execution path: approvals, budgets, quotas, retention,
  erasure, tracing and progress events work per turn unchanged.
- Cost grows with the history; the character and turn bounds keep it predictable, and history counts against each
  run's `MaxTokens`. Stable history at the start of the prompt suits provider prompt caching.
- A follow-up that depends on details an earlier tool returned must let the agent read them again. That costs a tool
  call but never answers from stale or untrusted data.
- Once a conversation reads untrusted content, its later changes always wait for a person. Starting a new
  conversation resets this.
- Enforced by `AgentConversationTests` (unit: history, aggregate; integration: follow-up, busy and race, privacy,
  taint, limits, delete, erasure), `PendingModelChangesTests` and `TenantRowLevelSecurityCoverageTests`.

<!--#if (sample)-->

- The conversational sample `shipping.assistant` runs end to end over HTTP in `ShippingAssistantConversationE2ETests`.

<!--#endif-->

## Alternatives considered

- **Replay the full transcript of earlier turns (tool calls and results).** Gives the model more context, but grows
  fast, repeats old untrusted text into every later turn and answers from data that may have changed. Rejected.
- **One long-lived run for the whole conversation.** Would hold a lease and budgets across hours of human think time
  and mix the erasure, quota and approval of many requests into one row. Rejected.
- **Summarize old turns with the model.** Useful for very long conversations, but a summary is model output
  presented as history (an injection path) and costs a call. Deferred; the bounds drop old turns instead.
- **Taint only the turn that read untrusted content.** Leaves the next turn acting on an answer derived from that
  content without a person. Rejected.
- **Token streaming of answers in this change.** Independent of conversations; the progress events already show the
  steps. Deferred.

## Related

[Agentic module](../Modules/Agentic.md#conversations), [Agent security model](../Architecture/Agent-Security.md),
[ADR index](README.md), and `/starterkit:update-docs`.
