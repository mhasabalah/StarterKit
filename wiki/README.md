# StarterKit Wiki

Developer documentation for **StarterKit**, a production-ready ASP.NET Core 9 modular monolith template
(vertical slices, CQRS with MediatR, Carter endpoints, EF Core + PostgreSQL, Redis, MassTransit, Hangfire, .NET Aspire).

The full document list is in **[INDEX.md](INDEX.md)**. Agent/assistant guidance lives in the repo-root `CLAUDE.md` and `AGENTS.md`.

## Where to start

| You are... | Read |
|------------|------|
| New to the codebase | [Architecture/Overview.md](Architecture/Overview.md), [Architecture/ProjectStructure.md](Architecture/ProjectStructure.md) |
| Adding a feature | [Architecture/CodingStandards.md](Architecture/CodingStandards.md), [Architecture/DesignPatterns.md](Architecture/DesignPatterns.md) |
| Adding a module | [Modules/Catalog.md](Modules/Catalog.md) |
| Touching the database | [Architecture/DatabaseConventions.md](Architecture/DatabaseConventions.md), [Reference/Migrations.md](Reference/Migrations.md) |
| Designing endpoints | [Architecture/ApiGuidelines.md](Architecture/ApiGuidelines.md) |
| Deploying | [deployment/INDEX.md](deployment/INDEX.md) |

## Modules

| Module | Purpose | Docs |
|--------|---------|------|
| Identity | Users, roles, JWT + refresh tokens, admin management, phone/email verification | [Identity/README.md](Identity/README.md) |
| Attachment | File uploads, pending (anonymous) uploads, Local / Azure Blob / S3 storage | [Attachment/README.md](Attachment/README.md) |
| Subscriptions | Subscribers, plans, subscriptions, payment methods | [Subscriptions/](Subscriptions/PaymentMethods-Frontend-Integration.md) |
| Catalog | Sample module (Product aggregate) - the reference vertical slice | [Modules/Catalog.md](Modules/Catalog.md) |

Shared libraries: `Shared.Contracts`, `Shared.Core`, `Shared.Messaging`, `Shared.Email`, `Shared.Notification` - see [Shared/README.md](Shared/README.md).

## Conventions for this wiki

- Reference material any developer needs (standards, guidelines, runbooks) belongs here.
- Personal investigation notes, flow walkthroughs and session analyses belong in your own notes vault, not in the repo.
- Never use real hostnames, IPs or credentials in examples - use `api.example.com`, `YOUR_SERVER_IP`, `CHANGE_ME`.
