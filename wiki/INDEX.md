# Documentation index

This page links every page of the wiki. Keep it complete; `bash scripts/check-docs.sh` reports pages missing from
it. Conventions: `Title-Kebab.md` files, one canonical page per topic, placeholders only (`api.example.com`,
`YOUR_SERVER_IP`, `CHANGE_ME`).

```
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
├── Testing/                      testing strategy
├── Infrastructure/               Aspire, Redis
├── Shared/                       IHttpContextService
├── Reference/                    migrations, backlog, troubleshooting
└── deployment/                   VPS, CI/CD, runner, secrets, backups, compose, nginx, observability
```

## Getting started

- [README](README.md): wiki landing page (where to start, module table)
- [Getting-Started/New-Project.md](Getting-Started/New-Project.md): start a product from the template (`/starterkit:new-project`)

## Architecture

- [Overview](Architecture/Overview.md): the big picture
- [ProjectStructure](Architecture/ProjectStructure.md): solution and module layout
- [CodingStandards](Architecture/CodingStandards.md): C# house style, errors, async, logging, localization
- [DesignPatterns](Architecture/DesignPatterns.md): the patterns in use and the mediator pipeline
- [ApiGuidelines](Architecture/ApiGuidelines.md): routes, versioning, envelopes, errors, auth, rate limits
- [DatabaseConventions](Architecture/DatabaseConventions.md): schemas, types, indexes, filters, transactions, RLS
- [Messaging](Architecture/Messaging.md): domain vs integration events, outbox, transports
- [Observability](Architecture/Observability.md): logs, traces, metrics for developers
- [TechnologyStack](Architecture/TechnologyStack.md): libraries and versions
- [Feature-Flags](Architecture/Feature-Flags.md): runtime flags, evaluation order, `.RequireFlag`, admin API

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

## Modules

- [Catalog](Modules/Catalog.md): the **reference module**; wiring checklist; removing the sample
- [Tenancy](Modules/Tenancy.md): control plane
- [Subscriptions](Modules/Subscriptions.md): plans, subscriptions, billing
- [Notification](Modules/Notification.md): email and WhatsApp delivery
- [Webhooks](Modules/Webhooks.md): outbound customer webhooks (signed, retried, SSRF-guarded)

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

## Infrastructure and shared

- [Aspire](Infrastructure/Aspire.md) · [Redis](Infrastructure/Redis.md) · [IHttpContextService](Shared/README.md)

## Reference

- [Reference/README](Reference/README.md) · [Migrations](Reference/Migrations.md) · [Template backlog](Reference/Template-Backlog.md) · [Third-party licenses](Reference/Third-Party-Licenses.md) · [Troubleshooting PostgreSQL authentication](Reference/Troubleshooting-PostgreSQL-Authentication.md)

## Deployment and operations

- [deployment/README](deployment/README.md): overview and production checklist
- [Deploy to VPS](deployment/Deploy-to-VPS.md) · [CI/CD](deployment/CI-CD.md) · [Self-hosted runner](deployment/Self-Hosted-Runner.md)
- [Secrets](deployment/Secrets.md) · [Backups](deployment/Backups.md) · [Docker Compose](deployment/Docker-Compose.md)
- [Database migrations](deployment/Database-Migrations.md): one-shot migrator, advisory lock, least-privilege roles
- [Nginx](deployment/Nginx.md) · [Observability stack](deployment/Observability.md) · [Seq security](deployment/Seq-Security.md) · [Production testing](deployment/Production-Testing.md)

## By task (Claude Code skills)

| Task | Skill | Read |
|---|---|---|
| New module | `/starterkit:new-module` | [Catalog](Modules/Catalog.md) |
| New entity or aggregate | `/starterkit:add-aggregate` | [DatabaseConventions](Architecture/DatabaseConventions.md) |
| New endpoint or slice | `/starterkit:add-feature` | [ApiGuidelines](Architecture/ApiGuidelines.md) |
| Migration | `/starterkit:add-migration` | [Migrations](Reference/Migrations.md) |
| Cross-module event | `/starterkit:add-integration-event` | [Messaging](Architecture/Messaging.md) |
| Background job | `/starterkit:add-background-job` | [Messaging](Architecture/Messaging.md), CLAUDE.md |
| Permission | `/starterkit:add-permission` | [Permissions-2FA](Identity/Permissions-2FA-and-Admin-Audit.md) |
| Email | `/starterkit:add-email-template` | [Notification](Modules/Notification.md) |
| Tests | `/starterkit:write-tests` | [Testing strategy](Testing/Testing-Strategy.md) |
| Review | `/starterkit:review` | CLAUDE.md guardrails |
| Docs | `/starterkit:update-docs` | this page |
