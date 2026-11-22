# 0034. Serve agent tools to external MCP clients through the tool gateway, as tainted calls

Agents with `McpServer = true` offer their tools to external MCP clients (IDEs, desktop assistants, other agent
platforms) at `/api/v1/mcp/{agentKey}`. Each call is decided by the same gateway as a run, treated as tainted, and
recorded as a run.

## Status

- **Status:** Accepted
- **Date:** 2026-11-22

## Context

People increasingly work through their own AI clients. The Model Context Protocol (MCP) is how those clients discover
and call tools. The application already has typed, authorized tools for its agents. Offering them over MCP is cheap,
but an MCP client is a model we do not control: its prompts, its reasoning and what it read are all unknown to us.

Constraints from earlier decisions:
- Every call goes through `ToolPolicy` (allowlist, the person's current permission, autonomy against risk, approval)
  ([ADR-0025](0025-agentic-module-and-model-seam.md)).
- Approval is consent to one payload, decided asynchronously ([ADR-0027](0027-agent-approvals-triggers-and-policies.md)).
- Model SDKs and AI frameworks stay inside Agentic.

## Decision

- **Opt in twice.** An agent sets `McpServer = true`, and the deployment sets `Agentic:Mcp:Enabled` (off by
  default; the route is not mapped otherwise).
- **The protocol.** `ModelContextProtocol.AspNetCore` (Agentic only, guarded by `ThirdPartyIsolationTests`) serves
  Streamable HTTP, stateless, so any replica answers any request. The route is
  `/api/v1/mcp/{agentKey}`.
  - `RequireTenantPermission(agentic.runs.start)`, plan feature `agents`, no impersonation.
  - A JWT or an API key, whose scopes narrow the person's permissions as everywhere.
- **The gateway decides.** `AgentMcpGateway`:
  - Offers the agent's allowlist only when the person may start the agent and the tenant policy has not disabled it.
  - Decides each tool with `ToolPolicy` as for a run that read untrusted content: the person's current permissions,
    the agent's autonomy tightened by the policy, and `tainted = true`.
  - Any change therefore needs a person, which a synchronous call cannot get, so it is refused with
    `approval_required` ("start a run of the agent instead"). In practice MCP clients get the read-only tools.
  - Arguments are bound strictly and validated by the tool's validators before anything runs.
- **Every call is on record.** A call that passes the gateway is a run (trigger `Mcp`, one tool step, no model turn),
  started through `AgentRunStarter`, so the policy, capacity and `agent_runs` quota apply.
  - It is executed in the request, in the person's tenant and branch scope, as the acting user, with the tool's
    timeout and the result size limit.
  - It raises no integration event and no notification.
  - A worker that ever finds such a run (the process stopped mid-call) fails it with `mcp_call_interrupted` instead of
    running it as an agent run.

## Consequences

- People can use the application's read tools from their own AI clients with their own identity, and the
  application's audit, retention, erasure and quotas cover those calls.
- Writes stay with agent runs and approvals. An MCP client that wants a change can only ask for it in the
  application.
- Each call costs one `agent_runs` unit. Plans that enable MCP should size the quota for it.
- Enforced by:
  - `AgentMcpTests` (integration): read tools only, call recorded as an Mcp run without events, changes refused,
    invalid arguments, who may start the agent, off, quota, tenant isolation, timeout, interrupted call.
  - `ThirdPartyIsolationTests.The_MCP_SDK_is_used_only_by_the_Agentic_module`.

<!--#if (sample)-->

- `ShippingAssistantMcpE2ETests` speaks the real protocol over HTTP to the sample assistant, authentication and
  authorization included.

<!--#endif-->

## Alternatives considered

- **MCP client (agents use external MCP servers).** A different feature with a different threat model (outside
  content and outbound calls). Deferred.
- **Let MCP clients request changes that wait for an approval.** An MCP call is synchronous. Holding it open for a
  human decision, or answering "pending" with no way to resume, would make a poor and confusing contract. Rejected;
  agent runs already offer approvals.
- **Stateful sessions.** They need sticky routing across replicas, and nothing here needs session state. Rejected.

## Related

[Agentic module](../Agentic/MCP-Server.md), [Agent security model](../Architecture/Agent-Security.md),
[ADR-0025](0025-agentic-module-and-model-seam.md), [ADR index](README.md).
