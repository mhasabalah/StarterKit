# 0033. Let agents delegate to other agents as child runs, never with more authority

An agent can hand a task to another agent with the built-in tool `agent.delegate`. The task becomes an ordinary run of
that agent: it acts for the same person and never has more autonomy than the run that delegated. The delegating run
waits for it without holding a worker, and gets its answer back as the tool's result.

## Status

- **Status:** Accepted
- **Date:** 2026-11-22

## Context

Agents were single units: a conversational assistant could only answer, and a task agent could only be started by a
person, a key or an event. Splitting work between agents is common, for example an assistant that hands a task to the
agent built for it. The obvious ways to do it would break earlier guarantees:
- Calling the other agent's tools directly would skip that agent's allowlist and instructions.
- Running it inside the tool call would hold a worker for minutes and lose durability
  ([ADR-0026](0026-durable-agent-runs-on-hangfire.md)).
- A system identity for the inner agent is forbidden.

## Decision

- **Opt in per agent.** `AgentDefinition.Delegates` lists the agents an agent may hand work to.
  - Each delegate must allow the new trigger `AgentTriggerKind.Delegated`.
  - Startup refuses unknown delegates, self-delegation and cycles.
  - The agent is then given `agent.delegate {agentKey, task}`, whose schema lists only its delegates.
- **A delegated run is a normal run.** It is started through `AgentRunStarter` (tenant policy, capacity, `agent_runs`
  quota) with `ParentRunId`, `ParentStepId` and `Depth`. It then has its own steps, approvals and budgets.
- **Never more authority:**
  - It acts for the same person (`InitiatedBy`), with the same permission ceiling and API key.
  - The person must hold the delegate's `RequiredPermission` at that moment.
  - Its autonomy is the lower of the two runs' autonomies.
  - It starts tainted when the delegating run is tainted.
  - It may use at most the delegating run's remaining tokens.
  - Nesting is bounded by `Agentic:Delegation:MaxDepth` (2; at most 3).
- **Waiting is durable.** The delegating run is saved as `AwaitingDelegate`, and its step as `AwaitingDelegate`, with
  no lease.
  - When the delegated run finishes, `ResumeParentOnDelegateFinishedEventHandler` turns its outcome (status, answer
    or typed result, failure code) into the step's result. It adds the delegated run's tokens to the parent's
    `DelegatedTokens` (they count against the parent's budget, but usage reports them once, on the delegated run),
    carries its taint over, and queues the parent again.
  - A parent that was still saving its suspension is caught by a delayed `ResolveAgentDelegationJob` (retried with
    backoff), by its own next attempt, or by `RequeueStalledAgentRunsJob`, which resumes any parent whose delegate
    already finished (a lost event).
  - A retried attempt finds the run it already started (unique index `ux_agent_runs_parent_step`) instead of
    starting another.
- **Cascades.**
  - Cancelling a run cancels the runs it delegated to (retried against a running delegate's checkpoints). A delegated
    run also stops at its next checkpoint once its parent has finished. A cancel requested while a run delegates wins
    over its suspension.
  - Erasing a run's content erases theirs.
  - A delegated run that finishes after its parent's content was erased is erased when it stops.
  - A conversation turn that waits for a delegate counts as its active turn.

## Consequences

- Agents compose without a new execution path. Approvals of a delegated run are decided like any other, and the
  delegating run waits.
- The delegating model sees only the delegate's final answer, never its tool results, the same rule as conversation
  history.
- A conversation turn that delegates stays busy until the delegated run, approvals included, finishes.
- An agent whose own tools only read still needs the autonomy of what it delegates.
- Enforced by:
  - `AgentDelegationTests` (unit): registry validation, the declaration, arguments, run rules.
  - `AgentDelegationTests` (integration): same person, lower autonomy, approval inside a delegate, depth,
    permission, allowlist, cancel cascade, no second child on retry, taint both ways, failed delegate, erasure
    cascade, tenant isolation.

<!--#if (sample)-->

- The Shipping assistant delegates "prepare it for clearance" to the preparation agent. It is `Execute` for that
  reason; its own tools remain read-only, and the submission still needs a person.

<!--#endif-->

## Alternatives considered

- **Expose the other agent's tools to the first agent.** Loses the delegate's instructions and allowlist, and mixes
  the risks of both agents into one run. Rejected.
- **Run the delegate synchronously inside the tool call.** Holds a worker, cannot survive a restart, and cannot wait
  for an approval. Rejected.
- **A coordinator service outside runs (a saga).** More machinery for the same states the run row already has.
  Rejected.

## Related

[Agentic module](../Modules/Agentic.md#delegation), [Agent security model](../Architecture/Agent-Security.md),
[ADR-0026](0026-durable-agent-runs-on-hangfire.md), [ADR-0027](0027-agent-approvals-triggers-and-policies.md),
[ADR index](README.md).
