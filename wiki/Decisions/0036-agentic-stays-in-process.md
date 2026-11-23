# 0036. Keep Agentic in the monolith, scale it with worker replicas, and extract it only when a named trigger appears

Agentic stays a module of the monolith. Modules use it only through `Agentic.Contracts` and its integration events.
It scales by running more replicas of the same image as agent workers. It becomes a separate service only when one of
the triggers listed below is actually present.

## Status

- **Status:** Accepted
- **Date:** 2026-11-23

## Context

Agentic is used like a shared platform service: any module registers agents and tools, starts runs and receives
results, and outside clients use it over HTTP and MCP. That raises the question whether it should be a separately
deployed service (or serverless functions) instead of a module.

What the code looks like today:
- **Tools run in-process.** A tool is an adapter that sends one of its owning module's commands or queries through
  `ISender`, inside the person's tenant and branch scope, as the acting user, re-checked by `ToolPolicy` before every
  call ([ADR-0025](0025-agentic-module-and-model-seam.md)). The boundary rules forbid HTTP or gRPC between modules.
- **Runs are already off the request path.** They are durable rows executed on the Hangfire queue `agentic` with
  leases and checkpoints ([ADR-0026](0026-durable-agent-runs-on-hangfire.md)). `Hangfire:Servers:Agentic` turns the
  agent worker on or off per replica, so agent work can run on dedicated worker replicas of the same image
  (`docker-compose.worker.yml`).
- **The seam is already narrow.** Modules see only `Agentic.Contracts` (`AddAgent`, `AddAgentTool`,
  `RequestAgentRunCommand`, `AddKnowledgeSource`, the run queries) and the outbox events
  `AgentRunCompleted/FailedIntegrationEvent`. `ModuleBoundaryTests` and `ThirdPartyIsolationTests` enforce this.
- **Every product inherits the choice.** A separate service would make every product deploy, secure, monitor and
  version one more service from the start, whatever its size.

## Decision

- **Agentic stays in-process.** Modules use it only through `Agentic.Contracts` and the integration events. Nothing
  outside the module references its implementation, its DbContext, `Microsoft.Extensions.AI` or a provider SDK.
- **Scale with worker replicas, not with a new service.** In production, run agent work on dedicated replicas
  (`Hangfire__Servers__Agentic__Enabled=false` on `api`, `true` on `worker`). Size them with
  `Hangfire:Servers:Agentic:WorkerCount` and the per-tenant concurrency limit
  ([Agentic operations](../deployment/Agentic-Operations.md#2-size-the-workers)).
- **Every model call is a run.** There is no synchronous "complete this prompt" contract for other modules. A one-off
  job, such as summarizing a text, is a small agent with no tools and a typed `OutputType`. That way permissions,
  quotas, audit, retention and cost tracking apply to every model call.
- **Extract only when a trigger is present.** Reopen this decision with a new ADR when at least one of these holds,
  backed by measurements or an actual requirement:

  | Trigger | What it would look like |
  |---|---|
  | Scaling that worker replicas cannot absorb | Agent load harms the shared database or the API even with dedicated workers and tuned limits |
  | Separate ownership | Another team owns agents and needs its own release cadence |
  | A different runtime | Agents must be built on a non-.NET stack |
  | Several products share one agent platform | Separately deployed products need the same agents and knowledge |
  | Isolation required by compliance | Model-provider traffic or prompt content must run in a separate network zone or account |

- **If extracted, extract the gateway, not the tools.** Only the Agentic module moves. Tools stay in their owning
  modules and are called back through an authenticated tool endpoint that keeps `ToolPolicy`, the person's current
  permissions, the tenant and idempotency keys. The MCP server ([ADR-0034](0034-agent-mcp-server.md)) is the existing
  precedent: an outside caller reaches tools only through the gateway.

## Consequences

- Tool calls stay one in-process `ISender` call. Identity, tenant scope, permissions and transactions need no
  propagation across a network.
- Agent work and API requests are isolated by replica role, without a second deployable, a service-to-service auth
  scheme or a distributed transaction.
- Agentic shares the database and the release with the rest of the system. A schema change or a provider-SDK update
  ships with everything else.
- Extracting later remains possible because the seam is the contracts and the events. Keeping it strict is what keeps
  that option open: `ModuleBoundaryTests`, `ThirdPartyIsolationTests` and `AgentToolArchitectureTests` fail on the
  shortcuts that would close it.

## Alternatives considered

- **A separate agent service now.** Every tool call would become a network call that has to carry the person, the
  tenant, the branch, permissions and an idempotency key. That rebuilds the boundary security rules in every module,
  adds a deployable to every product, and solves no problem present today. Rejected.
- **Serverless functions per agent or per run.** Runs last minutes, wait for approvals and resume from checkpoints.
  They need leases, the outbox and the tenant context, which the Hangfire workers already provide. Functions would
  need the same state in the database plus a cold-start and connection-pooling story for tenant data with RLS.
  Rejected.
- **A shared "AI client" library that each module calls directly.** It would put model SDKs in every module and
  bypass `ToolPolicy`, quotas, audit and retention. Rejected (and blocked by `ThirdPartyIsolationTests`).

## Related

[Agentic module](../Modules/Agentic.md), [Agentic operations](../deployment/Agentic-Operations.md),
[Modular monolith boundaries](../Architecture/Overview.md), [ADR-0025](0025-agentic-module-and-model-seam.md),
[ADR-0026](0026-durable-agent-runs-on-hangfire.md), [ADR index](README.md).
