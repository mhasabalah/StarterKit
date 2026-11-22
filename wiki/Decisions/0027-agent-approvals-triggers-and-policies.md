# 0027. Govern agents with payload-bound approvals, explicit triggers and tighten-only tenant policies

A person approves one exact action, an event starts a run only for a real user, and a tenant can only tighten an agent.

## Status

- **Status:** Accepted
- **Date:** 2026-11-13

## Context

[ADR-0025](0025-agentic-module-and-model-seam.md) decides every tool call in code and
[ADR-0026](0026-durable-agent-runs-on-hangfire.md) makes runs durable. Three things were still open:

- Sensitive and external actions must not happen without a person, and a run cannot hold a worker while it waits.
  An approval that is checked at one moment and used at another invites time-of-check/time-of-use bugs: the payload,
  the tool or the user's access may change in between, and a job may be delivered twice.
- Agents should also start from events and schedules, where there is no request and no caller, without creating a
  privileged "system" identity or an event loop that starts runs without bound.
- Tenants need a say over each agent (off, less autonomy, smaller budgets), and plans must bound cost.

The Model Context Protocol was evaluated here as a way to expose tools to outside clients.

## Decision

### Approvals

- `ToolPolicy` returns `RequireApproval` for sensitive and external tools, for any change after the run read
  untrusted content, and for any change when the tenant's policy asks for it. The executor then creates an
  `AgentApproval`, marks the step `AwaitingApproval`, and suspends the run (`AwaitingApproval`, no lease, no worker).
- An approval is bound to one payload: it stores a copy of the arguments and a SHA-256 over tool name, tool version
  and arguments. The approver must send the hash they were shown (`approval_payload_mismatch` otherwise).
- The approver needs `agentic.approvals.decide` AND the proposed tool's own permission, must be an interactive user
  (no API key, no impersonation), and with `Agentic:Approvals:RequireDifferentApprover` must not be the user the run
  acts for. `Version` gives optimistic concurrency.
- A decision queues the run again. Nothing is assumed from it: the executor runs the whole `ToolPolicy` again
  (allowlist, the initiator's current permission, autonomy), compares the hash with the step's current arguments
  and the tool's current version, and then, in ONE save under the run's lease, moves the approval to `Consumed` and
  the step to `Executing`. There is no window between checking and using, and an approval is used once.
- A changed tool version voids a decision (the action is proposed again). A rejection closes the step as `Rejected`
  with the reason and the run continues. A request not decided within `Agentic:Approvals:ExpireAfterHours` expires
  and fails the run (`approval_expired`). When a run finishes in any way, its open requests are closed; a cancel
  request that arrives before a suspension wins.
- `AgentApproval` and `AgentPolicy` are `IAuditable`: requests and decisions are in the tenant audit trail. The
  trail carries the payload's digest (the property is named `PayloadDigest` because the audit interceptor skips
  names containing "Hash") and not the arguments, which stay in the run's transcript only.

### Triggers

- `RequestAgentRunCommand` (trusted, not bound to an endpoint) starts runs for `Event` and `Schedule`. The agent's
  definition must list the trigger kind (`AllowedTriggers`), and the tenant's policy must allow automatic triggers.
- There is no system identity. The run acts for `OnBehalfOfUserId`, who must be an active member holding
  `agentic.runs.start` and the agent's own permission at that moment, and is re-checked for every tool call like any
  other run.
- `TriggerKey` is unique per tenant and agent: a redelivered trigger returns the existing run. Automatic runs of one
  agent are limited per tenant and hour (`Agentic:Triggers:MaxAutomaticRunsPerHour`).

### Policies, quotas, capacity

- `AgentPolicy` (one row per tenant and agent) can switch an agent off, lower its autonomy, lower its budgets, refuse
  automatic triggers and demand approval for every change. `AgentRunStarter` always takes the STRICTER of policy and
  definition, so a policy can never widen an agent. Values are copied onto the run at start.
- Plan quotas use the existing `IQuotaService`: `agent_runs` (one per run, consumed last before the run is saved;
  there is no refund if that save then fails) and `agent_tokens` (per saved model turn, the last one included; a
  used-up plan stops the run with `quota_exceeded` before its next step). Capacity:
  `Agentic:Limits:MaxConcurrentRunsPerTenant` and the plan limit `max_concurrent_agent_runs`.
- Usage is read from the run rows (`GET /agent-usage`). The only other usage rows are model calls made outside a run
  (conversation summaries, [ADR-0031](0031-conversation-summaries.md)).

### Events and progress

- `AgentRunCompletedIntegrationEvent` / `AgentRunFailedIntegrationEvent` go through the outbox in the finishing save.
  Progress is a throttled `agentic.run-progress` user event; in-app notifications use the category group `agents`.
  None of them carries prompts, arguments or results, and none is the source of truth.

### MCP

- Not adopted now. Internal tools stay typed C# classes. An MCP endpoint, if a product needs outside clients to call
  tools, must be a thin adapter that starts a run or calls the same gateway with the caller's own identity; it may not
  execute tools directly. No consumer needs it today.

## Consequences

- A person's consent covers exactly what they saw; a tampered payload, a redeployed tool, a replayed job or a
  second decision cannot turn it into something else.
- Approval never replaces authorization: an approved action is still refused when the initiator lost the permission.
- Event-driven runs are attributable to a real user in every audit entry and can do nothing that user could not.
- The rate limit, capacity and quota checks are counts, not locks: two simultaneous starts may overshoot by one. The
  per-run budgets are the hard bounds.
- An automatic trigger whose user lost access is refused with 403; a consumer should treat that as "nothing to do"
  or retry after a new user is designated.
- Pinned by `Agentic.IntegrationTests` (`AgentApprovalTests`, `AgentGovernanceTests`, `AgentRunEventsTests`) and
  `Agentic.Tests/AgentApprovalTests`.

## Alternatives considered

- **Blocking the worker until someone approves:** holds a Hangfire worker for hours and loses the wait on a restart.
- **Approving by run id or step id only:** consent would follow whatever the step contains when it runs.
- **Letting the model resubmit an approved action:** a new call is a new payload; it needs its own decision.
- **A service account for automatic runs:** a standing privileged identity that no person answers for.
- **Policies that can raise autonomy or budgets:** turns tenant data into a way around code review.
- **A usage ledger table:** duplicates what the run rows already record.

## Related

[Agentic module](../Modules/Agentic.md), [ADR-0025](0025-agentic-module-and-model-seam.md),
[ADR-0026](0026-durable-agent-runs-on-hangfire.md), [ADR-0011](0011-audit-trail.md).
