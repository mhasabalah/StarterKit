# 0030. Stream agent answers as a preview over the notification hub

The model's answer is sent to the person who started the run while the model writes it. The stream is a preview: the
saved run stays the answer.

## Status

- **Status:** Accepted
- **Date:** 2026-11-22

## Context

A run shows its steps live (`agentic.run-progress`), but the text of an answer appeared only when the run finished.
In a conversation this makes every reply feel slow: a real model often writes for several seconds after it decided
not to call another tool. [ADR-0029](0029-agent-conversations.md) deferred streaming.

Constraints:
- Runs execute on a Hangfire worker, never in the request ([ADR-0026](0026-durable-agent-runs-on-hangfire.md)), so
  the request cannot stream the HTTP response.
- The run row and its steps are the only state, and a model turn may be repeated after a crash.
- Model output is untrusted, and an answer above `MaxModelTextBytes` is never stored.
- Progress events deliberately carry no content.

## Decision

- **The gateway streams to a sink.** `IAgentModelGateway.CompleteAsync(request, textSink, ct)` streams through
  `IChatClient.GetStreamingResponseAsync` when `Agentic:Streaming:Enabled` and the model's `Streaming` are on.
  - It forwards text deltas to the sink and assembles the updates into the same response a non-streamed call
    returns (text, tool calls, usage, finish reason).
  - The executor's checkpoint does not change.
- **No free turns.** A provider that reports no usage while streaming gets its tokens estimated (about four
  characters per token) for that turn, and that model is no longer streamed by the process.
- **The run's text stream.** `AgentRunTextStream` sends the user event `agentic.run-text` to the initiator over the
  notification hub (`IUserEventPublisher`).
  - Payload: `runId`, `conversationId`, `turn`, `offset`, `delta`.
  - Pieces are collected for `Agentic:Streaming:IntervalMilliseconds` (100) and the rest is sent when the turn ends.
- **A preview, never the source of truth.** A retried model turn streams again with the same `turn` from offset 0,
  so a client starts that turn over. An answer above `MaxModelTextBytes` stops streaming, because it is never stored
  either. The final answer is the saved run.
- **Who streams.** Only runs started by a person or an API key. Event and schedule runs have nobody watching.
- **Content on purpose.** This is the one live event that carries content: the initiator's own answer, sent only to
  the initiator's own sessions. It can be switched off globally or per model.

## Consequences

- Chat clients show the answer as it is written; nothing else changes for runs, approvals, budgets or erasure.
- Hub traffic grows with answer length, bounded by the interval and the text limit. Split deployments need the
  SignalR Redis backplane, as for progress.
- A client must treat the stream as a preview: reset on offset 0, and read the run (or the conversation) for the
  final text.
- Tool calls are not streamed; the progress event shows them when they are saved.
- Enforced by:
  - `StreamingTests` (unit): gateway parity, opt-out, no-usage fallback, batching, size cap, triggers, failed sends.
  - `AgentRunEventsTests` (integration): the stream adds up to the saved answer; nothing is sent when streaming is off.

## Alternatives considered

- **Server-sent events from the API.** The run executes on a worker, maybe another replica. The API would have to
  relay the worker's output through Redis or the database. The hub already reaches the user's sessions on every
  replica. Rejected.
- **Save partial text to the run row.** Writes a row many times per answer and stores text that may later be over
  the limit. Rejected.
- **Stream tool-call arguments too.** They are untrusted, are checked only after the answer, and a refused call must
  never look as if it ran. Rejected.

## Related

[Agentic module](../Agentic/Events-And-Data-Lifecycle.md), [Agent security model](../Architecture/Agent-Security.md),
[ADR-0029](0029-agent-conversations.md), [ADR index](README.md).
