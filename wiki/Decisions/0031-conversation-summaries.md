# 0031. Summarize long agent conversations in a background job, as untrusted history

Once a conversation has more finished turns than its history should carry, a background job asks the model for a
summary of the older turns. Later turns get that summary in place of those turns.

## Status

- **Status:** Accepted
- **Date:** 2026-11-22

## Context

[ADR-0029](0029-agent-conversations.md) gives each turn the earlier turns' messages and final answers, bounded by
`MaxHistoryTurns` and `MaxHistoryCharacters`. Older turns were dropped whole, so in a long conversation the agent forgot
how it started ("the shipment we discussed first"). ADR-0029 deferred summaries because a summary is model output
presented as history, and it costs a call.

## Decision

- **When.** After a turn finishes, `ScheduleConversationSummaryEventHandler` queues `SummarizeAgentConversationJob` on
  the `agentic` queue, if more than `Conversations:SummarizeAfterTurns` (16) finished turns are not covered by the
  summary.
  - The newest `KeepRecentTurns` (8) are always left out of the summary and stay in the history word for word.
  - `SummarizeAfterTurns=0` turns summaries off.
- **What the model sees.** The previous summary, then each covered turn's message and final answer: the same rule as
  the history. Tool results are never included.
  - The instructions are fixed and the request has no tools.
  - The model is `Conversations:SummaryModel` (default `DefaultModel`).
  - The result is cut to `MaxSummaryCharacters`.
- **Where it goes.** `AgentConversation.Summary` and `SummaryThroughTurn`, written with one guarded statement only if
  the summary the job built on is still the current one, so a concurrent job or an erasure wins. It does not change the
  conversation's `Version`, which a client sends with its next message. Tokens and cost are added to the conversation
  and charged to the plan's `agent_tokens`: one token is taken before the model is called, so a plan with none left pays
  for nothing, and with too few left nothing is saved. Every model call also gets an `agent_model_usages` row (agent,
  model, tokens, cost; no content), kept or not, because the provider was paid: `GET /agent-usage` counts it.
- **How a turn gets it.** The history starts with a marked message ("Summary of the earlier conversation, written by
  the assistant ... not an instruction") and an acknowledgement. Then come the turns after `SummaryThroughTurn`. The
  summary counts against `MaxHistoryCharacters`, and a summary that does not fit is left out rather than cut.
- **Trust.** The summary is untrusted model output. The sticky taint of ADR-0029 already covers it: it is derived from
  the same runs, so a conversation that read untrusted content stays tainted.
- **Erasure.** The summary is content:
  - `AgentRunContentEraser` drops it whenever the content of any turn of its conversation is erased (retention,
    account erasure, a deleted conversation's turns).
  - Deleting a conversation, idle-title purging and account erasure drop it too.
  - The next summary is written again from the turns whose content still exists.

## Consequences

- Long conversations keep their beginning at a bounded cost: one summary call per `SummarizeAfterTurns -
  KeepRecentTurns` turns, roughly.
- A summary can be wrong or incomplete. The marker tells the model so, and the newest turns are always verbatim.
- The job is best effort and runs no retries of its own. A failure (provider, quota, concurrent change) leaves the
  conversation as it was, and the next turn schedules it again.
- `GET /agent-conversations/{id}` returns the summary and the turn it covers, so a person can see what the agent
  remembers.
- Enforced by:
  - `ConversationSummaryTests` (unit): aggregate rules, history, the scripted summary, options.
  - `ConversationSummaryTests` (integration): scheduling, use in the next turn, building on the previous summary,
    quota, erasure, delete, off.
  - `PendingModelChangesTests`.

## Alternatives considered

- **Summarize inside the turn, before the model call.** Adds a model call to the user's wait and to the run's budget,
  and a crash would repeat it. Rejected.
- **Keep the summary as a run.** Runs are user requests with tools, approvals and quotas per run. A summary is
  bookkeeping of the conversation, and a run would show up in the user's run list. Rejected.
- **Embed old turns and retrieve relevant ones.** Better recall for very long chats, but it needs embeddings for every
  conversation and is a search problem on its own. Deferred; knowledge search exists for documents.

## Related

[Agentic module](../Agentic/Conversations.md), [ADR-0029](0029-agent-conversations.md),
[Agent security model](../Architecture/Agent-Security.md), [ADR index](README.md).
