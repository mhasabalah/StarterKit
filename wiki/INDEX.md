# Documentation index

This page links every page of the wiki.

Keep it complete; `bash scripts/check-docs.sh` reports pages missing from
it. Conventions: `Title-Kebab.md` files, one canonical page per topic, placeholders only (`api.example.com`,
`YOUR_SERVER_IP`, `CHANGE_ME`).

```text
wiki/
├── README.md                     landing page (start here)
├── Getting-Started/              new project from the template
├── Architecture/                 overview, structure, standards, patterns, API, database, messaging, observability
├── Decisions/                    ADRs
├── Modules/                      one page per module (Catalog = reference)
├── MultiTenancy/                 tenants, branches, isolation, operations
├── Identity/                     auth, users/admins API, permissions/2FA, IdP replacement, JWT
├── Attachment/                   uploads, storage providers, security, reliability, config
├── Subscriptions/                billing deep dive, payment-method frontend guide
├── Testing/                      testing strategy, k6 load testing
├── Infrastructure/               Aspire, Redis
├── Shared/                       IHttpContextService
├── Reference/                    migrations, backlog, troubleshooting
└── deployment/                   VPS, CI/CD, runner, secrets, backups, compose, nginx, observability
```

## Getting started

- [README](README.md): wiki landing page (where to start, module table)
- [Getting-Started/New-Project.md](Getting-Started/New-Project.md): start a product from the template (`dotnet new starterkit`, `/starterkit:new-project`)
- [Getting-Started/Templates.md](Getting-Started/Templates.md): the `dotnet new` product and module templates and their switches
<!--#if (templateSource)-->
- [Getting-Started/Changing-The-Template.md](Getting-Started/Changing-The-Template.md): template-repository rules: conditional code, module migration sets, the CI matrix
<!--#endif-->

## Architecture

- [Overview](Architecture/Overview.md): the big picture
- [System Design](Architecture/System-Design.md): the running system as views of one architecture model: landscape, containers, module map, deployment, scaling and failure posture, and a drill-down per module
- [Request lifecycle](Architecture/Request-Lifecycle.md): HTTP middleware, endpoint filters, mediator, commit and event dispatch
- [Errors and responses](Architecture/Errors-and-Responses.md): response envelopes, exception/status mapping, machine codes and trace identifiers
- [Concurrency and versions](Architecture/Concurrency-and-Versions.md): version tokens, stale updates, child collections and background conflicts
- [ProjectStructure](Architecture/ProjectStructure.md): solution and module layout
- [CodingStandards](Architecture/CodingStandards.md): C# house style, errors, async, logging, localization
- [DesignPatterns](Architecture/DesignPatterns.md): the patterns in use and the mediator pipeline
- [ApiGuidelines](Architecture/ApiGuidelines.md): routes, versioning, envelopes, errors, auth, rate limits
- [DatabaseConventions](Architecture/DatabaseConventions.md): schemas, types, indexes, filters, transactions, RLS
- [Messaging](Architecture/Messaging.md): domain vs integration events, outbox, transports
- [Observability](Architecture/Observability.md): logs, traces, metrics for developers
- [TechnologyStack](Architecture/TechnologyStack.md): libraries and versions
- [Feature-Flags](Architecture/Feature-Flags.md): runtime flags, evaluation order, `.RequireFlag`, admin API
<!--#if (agentic)-->
- [Agent security model](Architecture/Agent-Security.md): trust boundaries of AI agents, and each threat with its control and the test that proves it
<!--#endif-->

## Decisions (ADRs)

- [Decisions/README](Decisions/README.md): the index and the [template](Decisions/0000-template.md)
- [0001](Decisions/0001-modular-monolith-schema-per-module.md): modular monolith, schema per module
- [0002](Decisions/0002-carter-minimal-apis.md): Carter minimal APIs
- [0003](Decisions/0003-exceptions-not-result-type.md): typed exceptions, no Result type
- [0004](Decisions/0004-outbox-and-messaging-transports.md): outbox and transports
- [0005](Decisions/0005-tenant-isolation-model.md): tenant isolation model
- [0006](Decisions/0006-identity-behind-userdirectory-port.md): UserDirectory port
- [0007](Decisions/0007-owned-mediator-port.md): owned mediator port (no MediatR)
- [0008](Decisions/0008-feature-flags.md): own feature-flag port (no Microsoft.FeatureManagement)
- [0009](Decisions/0009-outbound-webhooks.md): outbound customer webhooks module
- [0011](Decisions/0011-audit-trail.md): generic same-transaction audit trail
- [0012](Decisions/0012-realtime-inbox-and-push.md): in-app inbox, SignalR hub and FCM push as additive channels
- [0013](Decisions/0013-enterprise-sso-and-scim.md): per-tenant SSO (OIDC/SAML) and SCIM inside Identity
- [0014](Decisions/0014-passkeys-on-built-in-identity.md): passkeys on .NET 10 Identity WebAuthn, server-side ceremonies
- [0015](Decisions/0015-malware-scanning-clamd-fail-closed.md): upload malware scanning with ClamAV, failing closed
- [0017](Decisions/0017-money-precision-and-per-currency-rounding.md): money as numeric(19,4), rounding per ISO 4217 currency
- [0018](Decisions/0018-off-session-dunning-retries-on-saved-cards.md): idempotent off-session card retries in dunning, sandbox vs live gateways
- [0016](Decisions/0016-bulk-data-transfer-on-hangfire.md): bulk import/export as leased, checkpointed jobs on their own Hangfire queue
- [0019](Decisions/0019-module-manifests-and-discovery.md): modules plug in through a discovered manifest (no Program.cs edits)
<!--#if (templateSource)-->
- [0020](Decisions/0020-dotnet-new-templates.md): `dotnet new` product and module templates (the repo is the template)
- [0024](Decisions/0024-documentation-site.md): canonical wiki, Starlight V1, independent Fumadocs V2, validation and release guidance
<!--#endif-->
- [0021](Decisions/0021-aggregate-version-covers-child-collections.md): the aggregate version covers its child collections
- [0022](Decisions/0022-production-message-transport.md): PostgreSQL is the default production message transport
- [0023](Decisions/0023-durable-sagas-on-module-dbcontexts.md): saga state is durable, in the owning module's DbContext
<!--#if (agentic)-->
- [0025](Decisions/0025-agentic-module-and-model-seam.md): AI agents in one module, behind typed tools and an owned model seam
- [0026](Decisions/0026-durable-agent-runs-on-hangfire.md): agent runs are leased, step-checkpointed Hangfire jobs
- [0027](Decisions/0027-agent-approvals-triggers-and-policies.md): payload-bound approvals, explicit triggers, tighten-only tenant policies
- [0028](Decisions/0028-agent-knowledge-and-memory-on-pgvector.md): agent knowledge (hybrid search on pgvector) and approval-gated memory
- [0029](Decisions/0029-agent-conversations.md): agent conversations (one run per turn, final-answer history, sticky taint)
- [0030](Decisions/0030-streaming-agent-answers.md): agent answers streamed to the initiator as a preview over the notification hub
- [0031](Decisions/0031-conversation-summaries.md): long agent conversations summarized in a background job, as untrusted history
- [0032](Decisions/0032-knowledge-hnsw-index.md): HNSW index of knowledge vectors per configured dimension, built after migrations
- [0033](Decisions/0033-agent-delegation.md): agents delegate to other agents as child runs, never with more authority
- [0034](Decisions/0034-agent-mcp-server.md): agent tools served to external MCP clients through the tool gateway, as tainted calls
<!--#endif-->
- [0035](Decisions/0035-architecture-model-likec4-and-excalidraw-hero.md): one LikeC4 architecture model for every system view, plus one hand-drawn big picture
<!--#if (agentic)-->
- [0036](Decisions/0036-agentic-stays-in-process.md): Agentic stays in the monolith, scales with worker replicas, and is extracted only on named triggers
<!--#endif-->

## Modules

<!--#if (sample)-->
- [Catalog](Modules/Catalog.md): the **reference module**; wiring checklist; removing the sample
<!--#endif-->
- [Tenancy](Modules/Tenancy.md): control plane
- [Subscriptions](Modules/Subscriptions.md): plans, subscriptions, billing
- [Notification](Modules/Notification.md): email, WhatsApp, in-app and push delivery
  - [Notification-Realtime-and-Push](Modules/Notification-Realtime-and-Push.md): in-app inbox, SignalR hub, FCM push devices
  - [Notification-Email-Design-System](Modules/Notification-Email-Design-System.md): tokens, components, dark mode, branding, plain text
- [Webhooks](Modules/Webhooks.md): outbound customer webhooks (signed, retried, SSRF-guarded)
- [DataTransfer](Modules/DataTransfer.md): bulk CSV/Excel import/export jobs (dry run, error report, resumable, own Hangfire queue)
<!--#if (agentic)-->
- [Agentic](Modules/Agentic.md): AI agents as durable runs that act only through typed, authorized tools (overview, the five rules, architecture, guide map)
  - [Runs and durable execution](Agentic/Runs.md): lifecycle, how a worker executes a run, leases, retries, budgets, acting as the person
  - [Tools and the tool gateway](Agentic/Tools-And-The-Gateway.md): what a tool declares, the decision order, autonomy against risk, taint, typed results
  - [Human approval](Agentic/Approvals.md): when a person is asked, the payload an approval binds, who decides, expiry
  - [Triggers, policies, quotas and usage](Agentic/Triggers-Policies-And-Quotas.md): automatic runs, tenant policies, plan quotas, the usage report
  - [Conversations, streaming and summaries](Agentic/Conversations.md): multi-turn chat, history, live answers, summaries
  - [Knowledge and memory](Agentic/Knowledge-And-Memory.md): indexing, hybrid search, the HNSW index, memories
  - [Delegation between agents](Agentic/Delegation.md): child runs that never gain authority
  - [MCP server](Agentic/MCP-Server.md): an agent's read tools for IDEs and desktop assistants
  - [Events and data lifecycle](Agentic/Events-And-Data-Lifecycle.md): integration events, notifications, progress, retention and erasure
  - [Models, providers and evaluation](Agentic/Models-And-Evaluation.md): the model seam, providers, proving a real model
  - [Adding an agent](Agentic/Adding-An-Agent.md): step by step, with a checklist
  - [Configuration](Agentic/Configuration.md): every setting, grouped
  - [API, permissions and observability](Agentic/API-And-Observability.md): routes, permissions, error codes, metrics and traces
  - [Failure modes and guarantees](Agentic/Failure-Modes.md): what happens when something goes wrong, and the tests that hold it
<!--#endif-->
<!--#if (sample)-->
<!--#if (agentic)-->
- [Shipping](Modules/Shipping.md): the agent **proof of concept** (shipment preparation: typed tools, event trigger, approval before submission)
<!--#endif-->
<!--#endif-->

## Multi-tenancy

- [Overview](MultiTenancy/Overview.md)
- [Branches and scoping](MultiTenancy/Branches-and-Scoping.md)
- [Isolation and dedicated databases](MultiTenancy/Isolation-and-Dedicated-Databases.md)
- [Operations](MultiTenancy/Operations.md)
- [Adding a tenant-aware module](MultiTenancy/Adding-a-Tenant-Aware-Module.md)

## Identity

- [README](Identity/README.md): module overview, OTP, email confirmation, security configuration
- [Users-and-Admins-API](Identity/Users-and-Admins-API.md): every Identity route with its authorization
- [Permissions, 2FA and admin audit](Identity/Permissions-2FA-and-Admin-Audit.md)
- [Passkeys](Identity/Passkeys.md): WebAuthn sign-in, MFA sessions, step-up, configuration
- [Enterprise SSO](Identity/Enterprise-SSO.md): per-tenant OIDC / SAML 2.0, verified domains, JIT, enforced SSO, IdP setup
- [SCIM](Identity/SCIM.md): SCIM 2.0 users and groups, deprovisioning, group → role mapping
- [Frontend integration](Identity/Frontend-Integration.md): email-first SSO, code exchange, WebAuthn browser code
- [Bot protection (CAPTCHA)](Identity/Bot-Protection.md): `.RequireHumanVerification`, Turnstile adapter, fail-open, adding a provider
- [Replacing the identity provider](Identity/Replacing-the-Identity-Provider.md)
- JWT: [technical guide](Identity/jwt/JWT-AUTH-TECHNICAL-GUIDE.md) · [frontend guide](Identity/jwt/JWT-AUTH-FRONTEND-GUIDE.md) · [token refresh strategies](Identity/jwt/TokenRefreshStrategies.md)

## Attachment

- [README](Attachment/README.md) · [Security](Attachment/Security.md) · [Reliability](Attachment/Reliability.md) · [Configuration](Attachment/Configuration.md)
- [Uploads](Attachment/Uploads.md): direct and pending (session) uploads
- [Storage providers](Attachment/Storage-Providers.md): Local / Azure Blob / S3
- [Image processing](Attachment/Image-Processing.md): thumbnails and image validation behind `IImageProcessingService` (SkiaSharp), how to switch libraries

## Subscriptions

- [Billing](Subscriptions/Billing.md): lifecycle, ledger, invoices, refunds
- [Payment methods: frontend integration](Subscriptions/PaymentMethods-Frontend-Integration.md)
- [Online payments](Subscriptions/Online-Payments.md): gateway port, idempotent webhook inbox, adding a real provider

## Testing

- [Testing strategy](Testing/Testing-Strategy.md): levels, what every feature needs, running tests (`/starterkit:write-tests`)
- [Load testing](Testing/Load-Testing.md): k6 HTTP load scenarios against a LoadTest-profile stack, thresholds, baseline numbers, nightly CI

## Infrastructure and shared

- [Aspire](Infrastructure/Aspire.md) · [Redis](Infrastructure/Redis.md) · [IHttpContextService](Shared/README.md)

## Reference

- [Configuration](Reference/Configuration.md): base settings, Docker/Development overrides and option meanings
- [Endpoint authorization](Reference/Endpoint-Authorization.md): every Carter route, host surfaces, access policy, filters and role grants
- [Architecture guardrails](Reference/Architecture-Guardrails.md): every architecture assertion, repair and related repository rule

- [Diagram style](Reference/Diagram-Style.md): semantic roles, accessible architecture views, examples and rendering checks
- [Feature catalog](Reference/Features.md): everything the template ships, by area
<!--#if (templateSource)-->
- [Brand](Reference/Brand.md): logos, palette, typography and voice
- [Template backlog](Reference/Template-Backlog.md): known issues and roadmap of the template
<!--#endif-->
- [Reference/README](Reference/README.md) · [Migrations](Reference/Migrations.md) · [Third-party licenses](Reference/Third-Party-Licenses.md) · [Troubleshooting PostgreSQL authentication](Reference/Troubleshooting-PostgreSQL-Authentication.md)

## Deployment and operations

- [deployment/README](deployment/README.md): overview and production checklist
- [Deploy to VPS](deployment/Deploy-to-VPS.md) · [CI/CD](deployment/CI-CD.md) · [Self-hosted runner](deployment/Self-Hosted-Runner.md)
- [Secrets](deployment/Secrets.md) · [Backups](deployment/Backups.md) · [Docker Compose](deployment/Docker-Compose.md)
- [Database migrations](deployment/Database-Migrations.md): one-shot migrator, advisory lock, least-privilege roles
- [Nginx](deployment/Nginx.md) · [Observability stack](deployment/Observability.md) · [Seq security](deployment/Seq-Security.md) · [Production testing](deployment/Production-Testing.md)
<!--#if (agentic)-->
- [Agentic operations](deployment/Agentic-Operations.md): switching agents on with a real provider, sizing workers, signals, and what to do for each agent alert
<!--#endif-->

## Rulebook coverage

Use this map with `CLAUDE.md`: the rulebook defines constraints, while these canonical pages explain their application and limits.

| Topic | Canonical page |
|---|---|
| Module boundaries, Contracts dependencies, provider-neutral users, no inter-module HTTP | [Architecture overview](Architecture/Overview.md), [Replacing Identity](Identity/Replacing-the-Identity-Provider.md) |
| Slice layout, grouped lookups, context locations and naming | [Project structure](Architecture/ProjectStructure.md), [Coding standards](Architecture/CodingStandards.md) |
| Command repositories, query projections and pagination | [Design patterns](Architecture/DesignPatterns.md), [Database conventions](Architecture/DatabaseConventions.md) |
| Schema, snake_case, string enums, soft delete and child keys | [Database conventions](Architecture/DatabaseConventions.md) |
| Manifest discovery, module order and startup registration | [Architecture overview](Architecture/Overview.md), [ADR 0019](Decisions/0019-module-manifests-and-discovery.md) |
| Middleware order, automatic validation and mediator behaviors | [Request lifecycle](Architecture/Request-Lifecycle.md), [Design patterns](Architecture/DesignPatterns.md) |
| Typed exceptions, status mapping, envelopes and machine codes | [Errors and responses](Architecture/Errors-and-Responses.md) |
| HTTP idempotency, replay, conflicts and Redis failure behavior | [Request lifecycle](Architecture/Request-Lifecycle.md) |
| Plan feature gates, quotas and paying versus managing subscribers | [Subscriptions](Modules/Subscriptions.md), [Billing](Subscriptions/Billing.md) |
| Runtime flags versus entitlements | [Feature flags](Architecture/Feature-Flags.md) |
| Human verification and CAPTCHA failures | [Bot protection](Identity/Bot-Protection.md) |
| Deploy-time migration, seeding and advisory locks | [Database migrations](deployment/Database-Migrations.md), [Migrations](Reference/Migrations.md) |
| Committed domain events, outermost dispatch and background units of work | [Request lifecycle](Architecture/Request-Lifecycle.md), [Messaging](Architecture/Messaging.md) |
| Version contracts, child-only changes, uniqueness and background conflicts | [Concurrency and versions](Architecture/Concurrency-and-Versions.md) |
| TimeProvider and nullable CreatedAt | [Coding standards](Architecture/CodingStandards.md), [Database conventions](Architecture/DatabaseConventions.md) |
| Explicit authorization, impersonation and API-key restrictions | [Endpoint authorization](Reference/Endpoint-Authorization.md), [API guidelines](Architecture/ApiGuidelines.md) |
| Role-derived permissions, seeded roles, API-key scope intersection and MFA | [Permissions, 2FA and audit](Identity/Permissions-2FA-and-Admin-Audit.md), [Identity](Identity/README.md) |
| Passkey ceremony state and MFA sign-in | [Passkeys](Identity/Passkeys.md) |
| SSO entitlement/enforcement, verified domains, one-time exchange and guarded outbound URLs | [Enterprise SSO](Identity/Enterprise-SSO.md), [Shared projects](Shared/README.md) |
| Directory role validation, provisioning and suspension on offboarding | [SCIM](Identity/SCIM.md), [Tenancy](Modules/Tenancy.md) |
| Notification builder, forced security email and additive channel jobs | [Notification](Modules/Notification.md) |
| Push-only hub, session revocation, URL tokens, devices and FCM isolation | [Real-time and push](Modules/Notification-Realtime-and-Push.md), [JWT technical guide](Identity/jwt/JWT-AUTH-TECHNICAL-GUIDE.md) |
| Email tokens, components and preheaders | [Email design system](Modules/Notification-Email-Design-System.md) |
| Categories, fixed channels, mutes and quiet hours | [Real-time and push](Modules/Notification-Realtime-and-Push.md#mutes-and-quiet-hours) |
| Bilingual resources, IdentityText, LocalizedText and enum lookup | [Coding standards](Architecture/CodingStandards.md), [API guidelines](Architecture/ApiGuidelines.md) |
| Same-save audit trail, ignored secrets and retention | [Database conventions](Architecture/DatabaseConventions.md) |
| Fail-closed malware scanning and degraded health | [Attachment security](Attachment/Security.md) |
| Bulk definitions, replayable batches, dry runs, worker queues and requester scope | [DataTransfer](Modules/DataTransfer.md) |
<!--#if (agentic)-->
| Agents, typed tools, the tool gateway, model providers and durable runs | [Agentic](Modules/Agentic.md), [ADR 0025](Decisions/0025-agentic-module-and-model-seam.md), [ADR 0026](Decisions/0026-durable-agent-runs-on-hangfire.md) |
| Agent approvals, automatic triggers, tenant policies, quotas and usage | [Approvals](Agentic/Approvals.md), [Triggers and quotas](Agentic/Triggers-Policies-And-Quotas.md), [ADR 0027](Decisions/0027-agent-approvals-triggers-and-policies.md) |
| Agent knowledge (indexing, hybrid search, citations), memory, content erasure and retention | [Knowledge and memory](Agentic/Knowledge-And-Memory.md), [Data lifecycle](Agentic/Events-And-Data-Lifecycle.md), [ADR 0028](Decisions/0028-agent-knowledge-and-memory-on-pgvector.md) |
| Conversations with agents (multi-turn chat, history, one turn at a time, deletion) and testing with a real model | [Conversations](Agentic/Conversations.md), [Models and evaluation](Agentic/Models-And-Evaluation.md), [ADR 0029](Decisions/0029-agent-conversations.md) |
| Agent threats (prompt injection, approval bypass, oversized output), alerts and runbook | [Agent security model](Architecture/Agent-Security.md), [Agentic operations](deployment/Agentic-Operations.md) |
<!--#endif-->
| Distributed caching, tenant keys and explicit invalidation | [Redis](Infrastructure/Redis.md) |
| HTTP context, Carter injection and ActingUser | [Shared projects](Shared/README.md) |
| Tenant entities, tenant-first indexes, filters last, interceptors and forced RLS | [Isolation and dedicated databases](MultiTenancy/Isolation-and-Dedicated-Databases.md), [Adding a tenant-aware module](MultiTenancy/Adding-a-Tenant-Aware-Module.md) |
| Role-based host bypass, pooling, Redis eviction and named filter bypasses | [Isolation and dedicated databases](MultiTenancy/Isolation-and-Dedicated-Databases.md), [Operations](MultiTenancy/Operations.md) |
| Ambient scopes, migrating tenants, default ownership, blob roots and seed ids | [Multi-tenancy overview](MultiTenancy/Overview.md), [Operations](MultiTenancy/Operations.md) |
| Domain versus integration versus synchronous reactions; existing event flows | [Messaging](Architecture/Messaging.md) |
| Transactional outbox, public webhook forwarding, headers, stable URNs and consumer idempotency | [Messaging](Architecture/Messaging.md), [Webhooks](Modules/Webhooks.md) |
| Durable module-owned sagas, registration, migrations and timeouts | [Messaging](Architecture/Messaging.md#sagas), [ADR 0023](Decisions/0023-durable-sagas-on-module-dbcontexts.md) |
| Dead letters, retry limits, admin operations and alerts | [Messaging](Architecture/Messaging.md), [Deployment observability](deployment/Observability.md) |
| Production transports, startup waiting, tenant redelivery and fault queues | [Messaging](Architecture/Messaging.md) |
| Every architecture guardrail, including migration and RLS E2E checks | [Architecture guardrails](Reference/Architecture-Guardrails.md) |
| Test stack, levels, fixtures, failure cases and tenant scopes | [Testing strategy](Testing/Testing-Strategy.md) |
| Every module and its owned capabilities | [Modules](#modules), [Feature catalog](Reference/Features.md) |
| Product switches: sample, agentic, multitenancy, transport, storage, ai-tooling, ci, deploy, github-owner | [Templates](Getting-Started/Templates.md#new-product) |
| Module switches: data-plane, outbox, example | [Templates](Getting-Started/Templates.md#new-module) |
| Every starterkit skill and its canonical guide | [By task](#by-task-claude-code-skills) |

## By task (Claude Code skills)

| Task | Skill | Read |
|---|---|---|
<!--#if (sample)-->
| New module | `/starterkit:new-module` | [Catalog](Modules/Catalog.md) |
<!--#else-->
| New module | `/starterkit:new-module` | [Templates](Getting-Started/Templates.md) |
<!--#endif-->
| New entity or aggregate | `/starterkit:add-aggregate` | [DatabaseConventions](Architecture/DatabaseConventions.md) |
| New endpoint or slice | `/starterkit:add-feature` | [ApiGuidelines](Architecture/ApiGuidelines.md) |
| Migration | `/starterkit:add-migration` | [Migrations](Reference/Migrations.md) |
| Cross-module event | `/starterkit:add-integration-event` | [Messaging](Architecture/Messaging.md) |
<!--#if (agentic)-->
| AI agent or agent tool | `/starterkit:add-agent-tool` | [Adding an agent](Agentic/Adding-An-Agent.md), [Agent security model](Architecture/Agent-Security.md) |
<!--#endif-->
| Background job | `/starterkit:add-background-job` | [Messaging](Architecture/Messaging.md), CLAUDE.md |
| Permission | `/starterkit:add-permission` | [Permissions-2FA](Identity/Permissions-2FA-and-Admin-Audit.md) |
| Email | `/starterkit:add-email-template` | [Notification](Modules/Notification.md) |
| Tests | `/starterkit:write-tests` | [Testing strategy](Testing/Testing-Strategy.md) |
| Review | `/starterkit:review` | [Architecture guardrails](Reference/Architecture-Guardrails.md) |
| New product | `/starterkit:new-project` | [New project](Getting-Started/New-Project.md) |
| Docs | `/starterkit:update-docs` | this page |
