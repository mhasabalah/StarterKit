# 0025. Run AI agents in one module, behind typed tools and an owned model seam

Agents act only through typed, authorized tools; one module owns the runtime, and models sit behind `IChatClient`.

## Status

- **Status:** Accepted
- **Date:** 2026-11-13

## Context

Products built on the template need AI agents that can do real work: read business data, plan several steps and
call business operations. The template already has what such work must go through: commands and queries behind the
owned mediator, validation, tenant isolation (EF filters and forced RLS), tenant permissions, the audit trail and the
outbox. An agent must use that path, not a second one beside it.

Three things are untrusted in this setting: the model's output, the content it reads (documents, tool results), and
any instruction found in either. Authorization therefore cannot depend on what a model was told.

The .NET options at the time of the decision: `Microsoft.Extensions.AI` (stable, 10.x) as a provider-neutral chat
abstraction implemented by the OpenAI and the official Anthropic SDKs; Microsoft Agent Framework 1.x (stable since
April 2026) with its own agent loop, workflows and checkpoint store; Semantic Kernel (being superseded by Agent
Framework); and the Model Context Protocol C# SDK 2.x.

## Decision

- **One module, `Agentic`** (tenant data, schema `agentic`, `ModuleOrder.Agentic` = 800). The registry, the run
  executor, the tool gateway and the model gateway are internal services of that module, not projects or deployments.
- **Business modules plug in through `Agentic.Contracts` only**: `IAgentTool<TInput, TOutput>` + `[AgentTool]`,
  `AgentDefinition`, `AddAgentTool<T>()`, `AddAgent(...)`. The contracts project references `Shared.Contracts` and no
  AI package. Agentic never references a business module (the `DataTransfer` definition pattern).
- **Agents and tools are code**, registered at startup and validated then: unique names, every tool declares a
  permission or `AllowAnyMember`, a mutation always needs a permission, an agent lists only registered tools its
  autonomy can run. They are not rows a tenant edits.
- **A tool is an adapter over `ISender`.** It may not take a DbContext, a repository or a service provider. Business
  rules stay in the commands; instructions are never where a rule lives.
- **The model only requests.** The chat client is called with tool DECLARATIONS (name, description, schema) and is
  never wrapped with automatic function invocation. Tool calls come back as data, are saved as steps, and are executed
  by the run executor after `ToolPolicy` decided, in this order: the agent's allowlist, the initiator's CURRENT
  permission for the tool, the run's autonomy against the tool's risk, then whether a person must approve.
  Arguments are parsed strictly (unknown or missing members are errors) and run through the input type's validators.
- **Trusted context.** A tool input holds only what a model may choose. Tenant, user, run and idempotency key come
  from `AgentToolContext`, built from the saved run. A tool runs inside the initiator's `TenantScope` (current
  membership and branch scope) and `ActingUser`, so handlers see the same user and tenant as in that user's request.
- **Untrusted content taints the run.** After a tool marked `ReturnsUntrustedContent` ran, every later change needs
  a person, whatever the autonomy level.
- **Models sit behind `IAgentModelGateway`**, implemented over `IChatClient`. Adapters: OpenAI-compatible (OpenAI,
  Azure OpenAI's v1 endpoint, self-hosted servers), Anthropic, and `Scripted` (deterministic `IAgentScript`s for tests
  and demos; refused in Production). Agents refer to a model KEY (`Agentic:Models`); credentials live in configuration
  or Docker secrets, never in the database.
- **The module is inert unless `Agentic:Enabled=true`**: its routes answer 404 and queued runs fail.

## Consequences

- Adding an agent to a module is a definition, a few tool classes and their registration. No AI package, no prompt
  plumbing and no authorization code in the business module.
- An agent can never do more than its initiator: a permission removed in the middle of a run refuses the next call
  that needs it, and a removed or deactivated initiator stops the run.
- The provider clients use one `HttpClient` owned by `AgentChatClientFactory`, not `IHttpClientFactory`: the host's
  default resilience handler (30 s attempt timeout, 3 retries) suits small API calls, not model requests. Each
  provider client applies its own timeout and bounded retries (`Agentic:Providers:<name>`).
- Tool errors that are business answers (4xx typed exceptions, concurrency conflicts) are returned to the model with
  their code. They never contain exception details; unexpected failures are logged and reported with a generic
  message.
- Prompts, tool arguments and results are stored in the run's steps (a resumed run needs them) and are not written to
  logs or traces unless `Agentic:Telemetry:CaptureContent` is on.
- Enforced by `ThirdPartyIsolationTests` (model SDKs only in Agentic), `AgentToolArchitectureTests` (tools use the
  mediator), `ModuleBoundaryTests`, `TenantIsolationArchitectureTests` / `TenantRowLevelSecurityCoverageTests`, and
  the abuse cases in `Agentic.IntegrationTests/AgentRunSecurityTests`.

## Alternatives considered

- **Microsoft Agent Framework as the runtime:** its agent loop invokes tools in-process inside one call and keeps
  workflow state in its own checkpoint store. Our loop must save every step in tenant-scoped tables and stop between
  a model's request and the tool's execution for authorization and approval. It stays an option for multi-agent
  graphs later; it also builds on `IChatClient`, so the model seam would not change.
- **Semantic Kernel:** superseded by Agent Framework.
- **Automatic function invocation (`UseFunctionInvocation`):** executes tools inside the client call, before anything
  is saved or authorized.
- **Agent and tool definitions in the database:** editable prompts and tool lists become an authorization surface
  and bypass code review.
- **Giving tools a service provider or repositories:** a second, unchecked business layer.
- **MCP as the internal tool mechanism:** adds a protocol hop inside one process. Evaluated as an optional outer
  endpoint only; the tool gateway is the single entry such an endpoint would have to call.

## Related

[Agentic module](../Modules/Agentic.md), [ADR-0026](0026-durable-agent-runs-on-hangfire.md),
[ADR-0016](0016-bulk-data-transfer-on-hangfire.md), [ADR-0007](0007-owned-mediator-port.md).
