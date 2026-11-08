---
name: add-integration-event
description: Publish a cross-module fact through the transactional outbox and consume it in another StarterKit module - integration event contract with MessageUrn, AddIntegrationEvent in the aggregate, outbox setup + migration + RLS, idempotent order-independent MassTransit consumer. Also decides integration event vs domain event vs synchronous ISender call. Use for "notify module X when Y happens", "publish an event", "add a consumer", "react to event", "outbox".
---

# Add an integration event (outbox → MassTransit)

The full background is in `wiki/Architecture/Messaging.md`, which you should read once. This skill is the checklist.

## 1. Decide first

| The reaction... | Use |
|---|---|
| stays inside the same module | **Domain event** (`IDomainEvent` + `INotificationHandler`), see `add-aggregate` |
| is in another module and may happen *eventually*; the publisher must never fail because of it | **Integration event** (this skill) |
| must succeed for the command to succeed, or the caller needs an answer now | **Synchronous** `ISender.Send(<Other>.Contracts command/query)`, not an event |
| must run inside the same unit of work in another module (rare: audit-before-token) | In-process `.Contracts` `IDomainEvent`, as in `ImpersonationStartedEvent` |

## 2. Steps

Templates: [references/templates.md](references/templates.md).

1. **Contract.** Create `<Publisher>.Contracts/<Area>s/IntegrationEvents/<X><PastTense>IntegrationEvent.cs`.
   - `[MessageUrn("<module>.<event-name>.v1")]` must match `^[a-z]+\.[a-z0-9-]+\.v[0-9]+$` and be unique.
   - `public sealed class ... : IntegrationEvent`, with `{ get; init; }` properties holding ids and a minimal
     projection. No entities, no `TenantId` (the tenant travels as a header).
   - Never change a published URN. To evolve, add optional properties. For a breaking change, make a new type
     with `.v2`.
2. **Raise.** In the aggregate method, next to the domain event, call
   `AddIntegrationEvent(new <X>IntegrationEvent { ... })`. Never call `IPublishEndpoint.Publish` from feature
   code: that bypasses the outbox, so the event is lost on a crash and sent on a rollback.
3. **Outbox**, once per DbContext. Skip it if the context already has one.
   - In `OnModelCreating`, call `builder.AddTenantOutbox()` for a tenant-data context (before `ApplyTenantFilters`)
     or `builder.AddOutbox()` for a host context.
   - In `<M>Module.cs`, call `services.AddOutbox<<M>DbContext>()` after `AddCustomDbContext`.
   - Create migration `AddOutbox` with the `add-migration` skill. For tenant-data contexts, add
     `migrationBuilder.EnableTenantRowLevelSecurity("<schema>", "outbox_messages");`.
   - In `src/Api.Tests/Architecture/MessagingArchitectureTests.cs`, add the context name to the outbox
     expectation (around line 187).
4. **Consumer.** Create `<Reactor>/Domains/<Area>/Consumers/<Purpose>Consumer.cs` with `IConsumer<<X>IntegrationEvent>`.
   - **Idempotent:** at-least-once delivery means duplicates happen. `context.MessageId` equals `EventId`.
   - **Order-independent:** treat the event as a trigger. Re-read the current state from the owner via
     `ISender` + `.Contracts`; don't trust the payload or the arrival order.
   - **Outside the mediator:** if it saves, dispatch domain events with
     `IDomainEventDispatcher.DispatchPendingAsync` after `SaveChangesAsync`, or do the work in
     `IUnitOfWorkRunner.RunAsync`.
   - **Errors:** throw to retry a transient failure. Return normally for "nothing to do".
   - **Tenancy:** the message's tenant is restored as the ambient scope, so use `TenantScope.BeginHostScope()`
     only for deliberate cross-tenant work.
   - The reacting module's assembly is registered automatically through its manifest's `ConsumerAssemblies`
     (default: the module assembly).
     Every module is in it already; a brand-new module needs adding.
5. **Architecture test list.** Add the event name to
   `MessagingArchitectureTests.Integration_events_exist_and_are_found_by_the_scan` (around line 54).
6. **Docs.** Add the flow to the table in `wiki/Architecture/Messaging.md` §1 and in CLAUDE.md → *Messaging*.
7. **Tests**
   - The aggregate test asserts the event is added (`entity.IntegrationEvents` or equivalent).
   - The integration test asserts an outbox row exists after the command (see
     `Catalog.IntegrationTests/ProductLifecycleTests.OutboxRowsFor`).
   - The consumer unit test covers a duplicate delivery (no-op) and an out-of-order delivery.

## Sagas (a long-running reaction to several events)

When a consumer must remember progress across several events (fulfilment, onboarding, refunds), use a saga, not
hand-kept status columns. It is durable, in the module's own DbContext
(`wiki/Decisions/0023-durable-sagas-on-module-dbcontexts.md`, `wiki/Architecture/Messaging.md` → *Sagas*):
- the state class (+ `IMustHaveTenant` for tenant data), mapped with `SagaStateConfiguration<T>`, plus a migration with
  RLS (`/starterkit:add-migration`);
- `public override void ConfigureMessaging(IBusRegistrationConfigurator bus) =>
  bus.AddDurableSagaStateMachine<TMachine, TState, TDbContext>();` in the module manifest;
- never `bus.AddSagaStateMachine` / `AddSaga` (in-memory repository, guarded by `SagaArchitectureTests`).

## Guardrails (`src/Api.Tests/Architecture/`)

- `MessagingArchitectureTests`: events live in `.Contracts` under an `IntegrationEvents` namespace with a unique,
  valid URN; they are not mediator notifications; consumers live in module implementations; outbox contexts and
  registrations match.
- `BackgroundJobEventDispatchTests`: a consumer that saves must take `IUnitOfWorkRunner` or `IDomainEventDispatcher`.
- `SagaArchitectureTests`: sagas only through `AddDurableSagaStateMachine`, with the state mapped in a module DbContext.

## Verify

```bash
dotnet build StarterKit.slnx
dotnet test src/Api.Tests --filter "FullyQualifiedName~Messaging|FullyQualifiedName~BackgroundJob"
dotnet test src/Modules/<Publisher>/<Publisher>.IntegrationTests      # outbox row written (Docker)
```

To check delivery at runtime, run with `Messaging:Transport=PostgreSql` and watch the consumer log in Seq or the
Aspire dashboard. Dead letters are rows in `<schema>.outbox_messages` where `dead_lettered_at` is not null; the
requeue SQL is in the Messaging wiki.
