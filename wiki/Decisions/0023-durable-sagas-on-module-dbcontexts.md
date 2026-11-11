# 0023. Saga state is durable, in the owning module's DbContext

Explicit saga registration persists state in the owning module database with its migrations and tenant rules.

## Status

- **Status:** Accepted
- **Date:** 2026-11-08

## Context

The bus called `SetInMemorySagaRepositoryProvider()` and discovered sagas by assembly scanning. No saga existed yet, but
the first one would have run on the in-memory repository without any warning:

- its state would be lost on every restart and deploy;
- each replica would keep its own copy;
- nothing would fail until production.

MassTransit's EF Core repository needs one DbContext per saga, and this solution has one DbContext per module.

## Decision

- **Saga state is module state.** It lives in the owning module's DbContext, with that module's schema, migrations and
  tenant rules. Shared projects hold no saga tables.
- **Explicit registration only.** A module registers its sagas in its manifest's `ConfigureMessaging` (new on `IModule`,
  a no-op on `ModuleBase`) with
  `bus.AddDurableSagaStateMachine<TMachine, TState, TDbContext>()` (`Shared.Core.Messaging.Sagas`). The bus no longer
  scans for sagas and no longer sets an in-memory provider.
- **The helper fixes the repository semantics:**
  - **Pessimistic concurrency.** `SELECT ... FOR UPDATE` on PostgreSQL applies the events of one instance one after
    another, so they never lose an update.
  - **In-memory outbox on the saga endpoint.** Messages the saga publishes or sends leave only after its state is
    committed. A failed save sends nothing, and the redelivered inbound message replays the step.
- **The state is mapped with `SagaStateConfiguration<TState>`:** the `CorrelationId` key, with `ValueGeneratedNever`.
  - In a tenant-data context, the state implements `IMustHaveTenant`, and its table gets RLS in the migration.
  - The bus restores the message's tenant, `TenantEntityInterceptor` stamps it, and the named tenant filter scopes the
    lookup, so another tenant's message never finds the instance.
- **Guarded** by `Api.Tests/Architecture/SagaArchitectureTests`:
  - no MassTransit saga registration or in-memory repository call in modules or Shared, outside the helper;
  - every saga state type is mapped in a module DbContext and resolves a non-in-memory repository in the real host.
- **Behaviour is pinned** by `Shared.Core.Tests/Messaging/DurableSagaTests` on InMemory and PostgreSql transports with
  real PostgreSQL: restart, concurrent events, tenant isolation, no publish on a failed save.

## Consequences

- A saga costs a table and a migration in its module, like any other aggregate, and is backed up with it.
- Pessimistic locking serializes the events of one instance. That is right for workflow sagas. A saga that one hot
  instance receives thousands of events per second for is a sign to redesign it, not to switch modes.
- Saga timeouts (`Schedule`) use the bus's message scheduler, registered on every transport since 2026-11-08:
  `AddSqlMessageScheduler` on PostgreSql (durable, in the transport tables), the delayed message scheduler on InMemory,
  and on RabbitMQ only with the delayed exchange (`Messaging:RabbitMq:UseDelayedExchange`). Without it, a saga that
  schedules faults to `<endpoint>_error`.
- Courier activities are still discovered from the consumer assemblies. Routing slips carry their state in the message.

## Alternatives considered

- **One shared saga DbContext (`SagaDbContext`) in Shared.Messaging:** puts business state in a shared project, and
  gives up module migrations and tenant isolation.
- **Optimistic concurrency with an `xmin` token:** conflicting events then rely on the retry policy, and every state
  needs the token. Without a token, optimistic mode loses updates: the concurrency test fails when the helper is
  switched to it.
- **Keep scanning and set an EF provider globally (`SetEntityFrameworkSagaRepositoryProvider`):** needs one DbContext
  for all sagas, so it is the shared-context option again.

## Related

[Messaging](../Architecture/Messaging.md), [Architecture Guardrails](../Reference/Architecture-Guardrails.md).
