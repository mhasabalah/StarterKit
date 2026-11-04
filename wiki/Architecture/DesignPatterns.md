# Design patterns

This page lists the patterns StarterKit uses, where each lives, and when to use it. Every row points to real code.
Decisions behind the big ones are in [../Decisions/](../Decisions/README.md).

| Pattern | Where | Use it for |
|---|---|---|
| **Modular monolith** | `src/Modules/*`, one schema per module | Independent bounded contexts in one deployable ([ADR-0001](../Decisions/0001-modular-monolith-schema-per-module.md)) |
| **Vertical slice** | `Domains/<Aggregate>/Features/<Feature>/` | One use case = validator + handler + endpoint in one folder |
| **CQRS (owned mediator)** | `ICommand`/`IQuery` + `ICommandHandler`/`IQueryHandler` (`Shared.Contracts.CQRS`) | Commands change state through aggregates; queries project from the DbContext |
| **Aggregate + factory** | `Product.Create(...)`, `Aggregate<Guid>` | Invariants in one place; private setters |
| **Domain events** | `AddDomainEvent` → `DispatchDomainEventsInterceptor` → `IPublisher` after commit | Same-module reactions (cache invalidation, logging, emails) |
| **Integration events + transactional outbox** | `AddIntegrationEvent` → `outbox_messages` → MassTransit | Cross-module facts, at-least-once ([Messaging.md](Messaging.md)) |
| **Repository** | `Data/Repository/<X>Repository.cs`, one per aggregate root | Loading and saving aggregates with their children |
| **Unit of work runner** | `IUnitOfWorkRunner` (`Shared.Core/Data`) | DB work outside the mediator (jobs, consumers) with event dispatch |
| **Pipeline behaviors** | `Shared.Core/Behaviors` | Cross-cutting concerns around every request (see below) |
| **Options** | `<M>Options` + `services.Configure<T>(section)` | Typed configuration per module |
| **Strategy / provider** | Storage providers (Local/Azure/S3), email providers, messaging transports | Swappable infrastructure behind one interface |
| **Policy (authorization)** | `IAttachmentEntityAccessPolicy`, `IProductAuthorizationService` | Row-level rules owned by the module that owns the data |
| **Port / adapter** | `UserDirectory.Contracts` implemented by Identity | Replaceable identity provider ([ADR-0006](../Decisions/0006-identity-behind-userdirectory-port.md)) |
| **Optimistic concurrency** | `Version` + `ExpectVersion` + `AuditableEntityInterceptor` | Lost-update protection, 409 on stale writes |
| **Ambient context** | `ITenantContext` / `TenantScope` (AsyncLocal) | The current tenant flows through requests, jobs and messages |

## mediator pipeline

Outermost first. `RequestTelemetryBehavior` is registered by `AddCustomObservability` (host); the others by
`Shared.Core/Extensions/MediatorExtensions.cs` (every module, including test fixtures):

```
Request → RequestTelemetryBehavior → ValidationBehavior → ConcurrencyHandlingBehavior → LoggingBehavior → DispatchDomainEventsBehavior → Handler
```

| Behavior | Responsibility |
|---|---|
| `RequestTelemetryBehavior` | Span `{command|query} {Name}` + `mediatr.request.duration` histogram; registered by `AddCustomObservability` (host only), so it is outermost |
| `ValidationBehavior` | Runs every `IValidator<TRequest>`; failures become 400 with localized messages |
| `ConcurrencyHandlingBehavior` | Translates EF `DbUpdateConcurrencyException` into `ConcurrencyConflictException` (409), in one place |
| `LoggingBehavior` | Request name and timing, with structured logs and trace correlation |
| `DispatchDomainEventsBehavior` | After the outermost handler completes, publishes the domain events of committed saves |

The shared pipeline has **no transaction behavior**. A command commits through its repository's `SaveChangesAsync`,
and domain events of failed or rolled-back saves are discarded. Identity adds one module-specific behavior,
`AuditedCommandTransactionBehavior`, which wraps audited admin commands so the UserManager saves and the audit row
commit together.

## Choosing between patterns

- **Repository or DbContext?** Commands use a repository; queries and lookup CRUD use the DbContext directly.
- **Domain event, integration event, or `ISender`?** See the decision table in [Messaging.md](Messaging.md#1-which-kind-of-event).
- **Service or entity method?** If it needs only the aggregate's own state, make it an entity method. If it needs
  other aggregates, I/O or the current user, make it a domain service in `Domains/<X>/Services/`.
- **New abstraction?** Only when a second real implementation exists or is planned. Explicit beats clever.
