# Architecture Decision Records

Short records of the significant decisions behind StarterKit.

Add one whenever you change a rule in `CLAUDE.md`, swap
a core library, or change an architecture test. Copy [0000-template.md](0000-template.md), take the next number, and
link it here.

| Number | Title | Status | Date |
|---|---|---|---|
| [0001](0001-modular-monolith-schema-per-module.md) | Modular monolith with one schema and DbContext per module | Accepted | 2026-11-04 (recorded; the decision predates the ADR log) |
| [0002](0002-carter-minimal-apis.md) | Carter minimal APIs instead of MVC controllers | Accepted | 2026-11-04 (recorded) |
| [0003](0003-exceptions-not-result-type.md) | Typed exceptions mapped centrally, no Result type | Accepted | 2026-11-04 (recorded) |
| [0004](0004-outbox-and-messaging-transports.md) | Transactional outbox with a pluggable MassTransit transport | Accepted | 2026-11-03 |
| [0005](0005-tenant-isolation-model.md) | Tenant isolation: named EF filters + PostgreSQL RLS, shared or dedicated databases | Accepted | 2026-11-02 |
| [0006](0006-identity-behind-userdirectory-port.md) | Other modules see users only through the UserDirectory port | Accepted | 2026-11-03 |
| [0007](0007-owned-mediator-port.md) | Own the mediator port; do not depend on MediatR | Accepted | 2026-11-04 |
| [0008](0008-feature-flags.md) | Own the feature-flag port; store flags in Tenancy | Accepted | 2026-11-05 |
| [0009](0009-outbound-webhooks.md) | Deliver public integration events to tenants as signed outbound webhooks | Accepted | 2026-11-05 |
| [0011](0011-audit-trail.md) | Record entity changes with an opt-in audit trail written in the same SaveChanges | Accepted | 2026-11-05 |
| [0012](0012-realtime-inbox-and-push.md) | Deliver in-app and push as additive Notification channels over SignalR and FCM | Accepted | 2026-11-05 |
| [0013](0013-enterprise-sso-and-scim.md) | Run per-tenant SSO and SCIM inside Identity, without dynamic auth schemes | Accepted | 2026-11-05 |
| [0014](0014-passkeys-on-built-in-identity.md) | Build passkeys on ASP.NET Core Identity's built-in WebAuthn, with server-side ceremony state | Accepted | 2026-11-05 |
| [0015](0015-malware-scanning-clamd-fail-closed.md) | Scan uploads with ClamAV over its own TCP client, failing closed | Accepted | 2026-11-06 |
| [0017](0017-money-precision-and-per-currency-rounding.md) | Store money as numeric(19,4) and round per ISO 4217 currency in the domain | Accepted | 2026-11-06 |
| [0018](0018-off-session-dunning-retries-on-saved-cards.md) | Retry overdue renewals off-session on a saved card, idempotently, before the reminder | Accepted | 2026-11-06 |
| [0016](0016-bulk-data-transfer-on-hangfire.md) | Run bulk imports and exports as leased, checkpointed Hangfire jobs on their own queue | Accepted | 2026-11-06 |
<!--#if (templateSource)-->
| [0020](0020-dotnet-new-templates.md) | The repository is the product template; modules come from a generated, CI-verified module template | Accepted | 2026-11-08 |
| [0024](0024-documentation-site.md) | Render the canonical wiki with Starlight and publish through GitHub Pages | Accepted | 2026-11-10 |
<!--#endif-->
| [0019](0019-module-manifests-and-discovery.md) | Modules plug in through a manifest the host discovers from the build graph | Accepted | 2026-11-07 |
| [0021](0021-aggregate-version-covers-child-collections.md) | The aggregate version covers its child collections | Accepted | 2026-11-08 |
| [0022](0022-production-message-transport.md) | PostgreSQL is the default production message transport | Accepted | 2026-11-08 |
| [0023](0023-durable-sagas-on-module-dbcontexts.md) | Saga state is durable, in the owning module's DbContext | Accepted | 2026-11-08 |
<!--#if (agentic)-->
| [0025](0025-agentic-module-and-model-seam.md) | Run AI agents in one module, behind typed tools and an owned model seam | Accepted | 2026-11-13 |
| [0026](0026-durable-agent-runs-on-hangfire.md) | Run agents as leased, step-checkpointed Hangfire jobs | Accepted | 2026-11-13 |
| [0027](0027-agent-approvals-triggers-and-policies.md) | Govern agents with payload-bound approvals, explicit triggers and tighten-only tenant policies | Accepted | 2026-11-13 |
| [0028](0028-agent-knowledge-and-memory-on-pgvector.md) | Give agents knowledge and memory on PostgreSQL with pgvector, behind the same permissions | Accepted | 2026-11-14 |
| [0029](0029-agent-conversations.md) | Hold agent conversations as one run per turn, with final-answer history and sticky taint | Accepted | 2026-11-22 |
| [0030](0030-streaming-agent-answers.md) | Stream agent answers as a preview over the notification hub | Accepted | 2026-11-22 |
| [0031](0031-conversation-summaries.md) | Summarize long agent conversations in a background job, as untrusted history | Accepted | 2026-11-22 |
| [0032](0032-knowledge-hnsw-index.md) | Index knowledge vectors with HNSW per configured dimension, built after migrations | Accepted | 2026-11-22 |
| [0033](0033-agent-delegation.md) | Let agents delegate to other agents as child runs, never with more authority | Accepted | 2026-11-22 |
| [0034](0034-agent-mcp-server.md) | Serve agent tools to external MCP clients through the tool gateway, as tainted calls | Accepted | 2026-11-22 |
<!--#endif-->
| [0035](0035-architecture-model-likec4-and-excalidraw-hero.md) | Describe the running system with one LikeC4 architecture model, plus one hand-drawn big picture | Accepted | 2026-11-23 |

Background for these decisions: [Architecture overview](../Architecture/Overview.md).
