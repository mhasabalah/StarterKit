# StarterKit documentation

Developer documentation for **StarterKit**, a ASP.NET Core 10 modular monolith template: vertical
slices, CQRS through an owned mediator port, Carter endpoints, EF Core 10 + PostgreSQL, Redis, MassTransit with a transactional
outbox, Hangfire, optional multi-tenancy, and .NET Aspire.

**Every page is listed in [INDEX.md](INDEX.md).** Rules for humans and agents are in the repo-root
[`CLAUDE.md`](../CLAUDE.md).
<!--#if (aiTooling)-->
Step-by-step procedures are the [StarterKit plugin skills](../tools/claude/starterkit-plugin/README.md).
<!--#endif-->

## At a glance

StarterKit generates a product with separately owned modules in one ASP.NET Core host. Each module owns its schema; synchronous calls use contracts and the owned mediator, and eventual reactions use the transactional outbox. Optional integrations require product-specific configuration.

## Where to start

| You want to... | Read |
|---|---|
| New to StarterKit: understand the system | [Architecture/Overview](Architecture/Overview.md) → [ProjectStructure](Architecture/ProjectStructure.md) → [Decisions](Decisions/README.md) |
| Start a product from the template | [Getting-Started/New-Project](Getting-Started/New-Project.md) · [Templates](Getting-Started/Templates.md) (`dotnet new`) |
<!--#if (sample)-->
| Add a module | [Modules/Catalog](Modules/Catalog.md) (reference) · `/starterkit:new-module` |
<!--#else-->
| Add a module | [Templates](Getting-Started/Templates.md) (`dotnet new starterkit-module`) · `/starterkit:new-module` |
<!--#endif-->
| Building a feature: add an endpoint | [ApiGuidelines](Architecture/ApiGuidelines.md) · [CodingStandards](Architecture/CodingStandards.md) · `/starterkit:add-feature` |
| Touch the database | [DatabaseConventions](Architecture/DatabaseConventions.md) · [Migrations](Reference/Migrations.md) |
| React across modules | [Messaging](Architecture/Messaging.md) · `/starterkit:add-integration-event` |
| Store tenant data | [MultiTenancy/Adding-a-Tenant-Aware-Module](MultiTenancy/Adding-a-Tenant-Aware-Module.md) |
| Write tests | [Testing/Testing-Strategy](Testing/Testing-Strategy.md) |
| Debug in dev or prod | [Architecture/Observability](Architecture/Observability.md) |
| Operating it: deploy and maintain | [deployment/README](deployment/README.md) |

## Modules

| Module | Purpose | Schema | Tenant data? | Docs |
|---|---|---|---|---|
| Identity | Auth (JWT + rotating refresh), users, admins, roles + permissions, 2FA, OTP, impersonation | `identity` | no | [Identity/README](Identity/README.md) |
| Tenancy | Tenants, branches, memberships, tenant roles, invitations, domains, audit | `tenancy` | no (control plane) | [Modules/Tenancy](Modules/Tenancy.md) |
| Attachment | Uploads, pending uploads, Local / Azure Blob / S3, reconciliation | `attachment` | **yes** | [Attachment/README](Attachment/README.md) |
| Subscriptions | Plans, subscriptions, payments, invoices, refunds, ledger | `subscriptions` | no | [Modules/Subscriptions](Modules/Subscriptions.md) |
<!--#if (sample)-->
| Catalog | Sample: products, categories, tags, attributes, wishlist (the reference slice) | `catalog` | **yes** | [Modules/Catalog](Modules/Catalog.md) |
<!--#endif-->
| Notification | Email/WhatsApp fallback, templates, delivery log, in-app inbox, SignalR, FCM push | `notification` | no | [Modules/Notification](Modules/Notification.md) |
| Webhooks | Signed outbound deliveries, retries and delivery log | `webhooks` | **yes** | [Webhooks](Modules/Webhooks.md) |
| DataTransfer | CSV/XLSX import/export jobs, dry runs and resumable batches | `datatransfer` | **yes** | [DataTransfer](Modules/DataTransfer.md) |

The shared libraries are `Shared.Contracts`, `Shared.Core` and `Shared.Messaging`. They hold no business state.
`IHttpContextService` is documented in [Shared/README](Shared/README.md).

## Related

[Feature catalog](Reference/Features.md), [Request lifecycle](Architecture/Request-Lifecycle.md), [Errors and responses](Architecture/Errors-and-Responses.md), and `/starterkit:new-project`.
