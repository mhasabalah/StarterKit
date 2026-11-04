# StarterKit Constitution

This file lists the non-negotiable principles for every spec, plan and task produced with Spec Kit in this
repository. The detailed rules live in `CLAUDE.md`; the executable ones are the architecture tests in
`src/Api.Tests/Architecture/`. When this file and the code disagree, the architecture tests win, and this file gets
fixed.

## Core Principles

### I. Modular monolith with hard boundaries

Every business capability is a module under `src/Modules/<Module>` with its own `DbContext`, its own Postgres
schema and a `.Contracts` project.
- Modules talk to each other only through `ISender` plus the other module's `.Contracts`, or through integration
  events.
- Never reference another module's implementation, models or DbContext.
- Users are reached only through `UserDirectory.Contracts`.
- Shared projects hold no business state.

*Enforced by `ModuleBoundaryTests`.*

### II. Vertical slices, explicit over clever

One use case is one folder (`Domains/<Aggregate>/Features/<Feature>/`) with the validator and handler in one file
and a Carter endpoint.
- Command handlers go through repositories and aggregate methods.
- Query handlers project from the DbContext with pagination.
- Business rules live in entities (factories, private setters, domain events).
- Prefer plain, readable code over abstractions; add an abstraction only for a second real use.

*Enforced by `NamingConventionTests`.*

### III. Tenant isolation by default (NON-NEGOTIABLE)

Business data lives in `ITenantDataContext`s.
- Every entity is `IMustHaveTenant`.
- Every query filter is named.
- Unique indexes start with `tenant_id`.
- Every tenant table has Postgres row-level security.
- Code never sets `TenantId`, and never calls the parameterless `IgnoreQueryFilters()`.
- Cross-tenant work is explicit (`TenantScope.BeginHostScope()`).

*Enforced by `TenantIsolationArchitectureTests`.*

### IV. Reliable messaging

Facts other modules react to are integration events, raised on the aggregate and delivered through the
transactional outbox (at-least-once).
- Consumers are idempotent and order-independent.
- A reaction that must succeed for the command to succeed is a synchronous `ISender` call, not an event.
- Jobs, hosted services and consumers publish their domain events through `IUnitOfWorkRunner` or
  `IDomainEventDispatcher`.

*Enforced by `MessagingArchitectureTests` and `BackgroundJobEventDispatchTests`.*

### V. Tested at every level (NON-NEGOTIABLE)

Every feature ships with:
- unit tests (domain, handlers, validators)
- integration tests on real Postgres (Testcontainers) for SQL, concurrency and tenancy behaviour
- HTTP E2E tests for auth and cross-module flows

Every failure mode has a test: not found, forbidden, conflict, validation, stale version, cross-tenant access. The
architecture tests must stay green.

### VI. Secure by default

- Every endpoint declares its authorization explicitly (architecture test); Carter endpoints are deny-by-default.
- Platform features use fine-grained permissions; tenant features use membership permissions.
- Destructive actions refuse impersonation.
- Privileged roles require 2FA.
- No real credentials, hosts or IPs in the repository. Use `CHANGE_ME`, `api.example.com`, `YOUR_SERVER_IP`.

*Enforced by `AdminEndpointAuthorizationTests`.*

### VII. Correctness under concurrency and time

- Critical updates use optimistic concurrency (`long? Version`, `ExpectVersion`, `Version` in the DTO). A stale
  version returns 409.
- Nobody sets `Version` by hand.
- New code takes time from `TimeProvider`.
- Errors are typed exceptions mapped in one place (`GlobalExceptionHandler`) to the `ApiResponse` error envelope.

## Technology Constraints

- .NET 10, ASP.NET Core minimal APIs with Carter, an owned mediator (`Shared.Contracts.Mediation`), FluentValidation, Mapster, EF Core 10 + Npgsql
  (snake_case), PostgreSQL 16, Redis, MassTransit (InMemory / PostgreSql / RabbitMq), Hangfire, OpenTelemetry, .NET
  Aspire.
- All package versions are in `Directory.Packages.props`. Introducing a new library needs a reason and an ADR in
  `wiki/Decisions/`.
- User-facing text is localized (en + ar resx).

## Development Workflow

1. **Spec, then plan, then tasks, then implement.** Use the `/starterkit:*` skills for each kind of change: module,
   feature, aggregate, migration, event, job, permission, email, tests.
2. Run `dotnet build StarterKit.slnx`. It must pass, as must the relevant tests and `dotnet test src/Api.Tests`.
3. Update the documentation in the same change (`wiki/`, `README.md`, `CLAUDE.md`) when behaviour or structure
   changes.
4. **Agents never commit or push.** The user reviews the diff and commits.

## Governance

This constitution supersedes ad-hoc conventions. Any change to it, or to the rules in `CLAUDE.md`, needs an ADR in
`wiki/Decisions/` and an update to the matching architecture test where one exists. Plans must contain a
Constitution Check, and any deviation needs a written justification.

**Version**: 1.0.0 | **Ratified**: 2026-11-03 | **Last Amended**: 2026-11-03
