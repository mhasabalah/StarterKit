# 0026. Run agents as leased, step-checkpointed Hangfire jobs

An agent run is a row with an append-only step log; a leased Hangfire job advances it one checkpoint at a time.

## Status

- **Status:** Accepted
- **Date:** 2026-11-13

## Context

An agent run takes several model requests and tool calls, may wait for a person, and must survive a restart. It
cannot live in an HTTP request, and its conversation cannot live in memory. Delivery in this system is at-least-once
(Hangfire, the outbox, the bus), so exactly-once execution cannot be assumed.

The template already runs long, resumable work this way for bulk imports ([ADR-0016](0016-bulk-data-transfer-on-hangfire.md)):
a job row as the source of truth, a lease under optimistic concurrency, per-batch checkpoints, its own retry and a
dedicated Hangfire queue.

## Decision

- **State is `agent_runs` + `agent_run_steps`** in the Agentic schema. A step is a model turn or a tool call. The
  conversation sent to a model is rebuilt from the steps (`TranscriptBuilder`); nothing is kept between attempts.
- **Hangfire runs it** (`ExecuteAgentRunJob`, queue `agentic`, server `Hangfire:Servers:Agentic`, Hangfire's own
  retry off). The job claims a lease (`LeaseOwner` / `LeaseUntil`); every save under the lease also saves the run row,
  so its version covers the steps. A second delivery or a second worker finds a live lease and does nothing.
- **Checkpoints:** after every model answer (the turn and the tool calls it asked for, in one save) and after every
  tool call. A tool-call step is saved as `Executing` BEFORE the tool runs.
- **At-least-once, stated per case:**
  - a crash after a model answered but before the save repeats the model request (cost only);
  - a crash around an idempotent tool runs it again with the same `AgentToolContext.IdempotencyKey`;
  - a tool declared `Idempotent = false` that is found `Executing`, or fails unexpectedly, is never repeated: the run
    stops with `tool_outcome_unknown` for a person to resolve.
- **Retry is one layer:** a failed attempt returns the run to `Queued` and schedules the next with backoff (15 s,
  1 min, 4 min, capped at 15 min). `Agentic:MaxAttempts` consecutive failures without a checkpoint fail the run
  (`model_unavailable` or `internal_error`). A shutdown returns the run to the queue without counting a failure.
- **Budgets are fixed when the run starts** (module limits lowered by the agent): model turns, tool calls, tokens and
  active time. Reaching one fails the run with `budget_exceeded`. Calls beyond `MaxToolCallsPerTurn` in one turn are
  dropped.
- **Authority is re-read, not stored:** the initiator's membership and permissions are resolved at the start of every
  attempt and before every tool call.
- **Versions:** a run records the agent version it started with. An agent declares `MinResumableVersion`; an older
  run fails with `agent_version_retired` instead of continuing under different instructions.
- **Cancellation** (`CancelAgentRunCommand`): a queued run is cancelled at once; a running one at its next checkpoint.
- **Safety net:** `RequeueStalledAgentRunsJob` (recurring, per tenant) queues again every waiting or running run
  without a live lease that was not queued recently. It closes the gaps of commit-then-enqueue: a lost job, a retry
  that was recorded but not scheduled, a worker that died holding the lease.
- **The lease covers the operation:** before a model request or a tool call that may take longer than
  `Agentic:LeaseSeconds` (provider timeout times retries, the tool's timeout), the lease is extended to cover it.

## Consequences

- A restart, a provider outage or a duplicate delivery never loses a run and never duplicates the effect of an
  idempotent tool. Mutation tools carry the obligation to be idempotent per key, or to say they are not.
- A worker is held for the length of a model request. Agent runs have their own Hangfire server, so they never delay
  notifications or the default queue, and the same image can run them on worker replicas.
- Token and cost counters are at-least-once like everything else: a repeated model request is counted when its
  answer is saved, so only saved turns count.
- Failure messages on the run are localized texts chosen by code; provider and exception details go to the log only.
- Pinned by `Agentic.IntegrationTests/AgentRunRecoveryTests` (duplicate delivery, concurrent worker, tool failure and
  retry with the same key, interrupted non-idempotent tool, shutdown, provider outage) and
  `BackgroundJobEventDispatchTests`.

## Alternatives considered

- **Temporal (or another workflow engine):** strong guarantees, but a new cluster to operate, a second source of
  truth beside PostgreSQL and determinism rules for workflow code. Revisit if runs need timers of days across many
  steps, fan-out to thousands of activities, or versioned replay.
- **A MassTransit saga ([ADR-0023](0023-durable-sagas-on-module-dbcontexts.md)):** a saga instance holds a pessimistic
  lock while it handles a message; minute-long model requests fit consumers badly (ADR-0016 rejected them for the same
  reason). Sagas remain right for short event correlation.
- **The agent framework's own checkpointing:** state outside the tenant-scoped tables, RLS and retention.
- **A hosted service polling the table:** a second scheduler beside Hangfire, without its dashboard or queues.
- **Extracting the lease and retry code of DataTransfer into Shared.Core now:** two users are not yet a pattern;
  the third one should extract it.

## Related

[Agentic module](../Modules/Agentic.md), [ADR-0025](0025-agentic-module-and-model-seam.md),
[ADR-0016](0016-bulk-data-transfer-on-hangfire.md).
