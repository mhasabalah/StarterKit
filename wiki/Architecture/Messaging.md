# Messaging: domain events, integration events, outbox and transports

How modules talk to each other **asynchronously**, and how StarterKit guarantees an event is published **if and only if**
the change that raised it was committed.

> Sync vs async: a module that needs an answer *now* sends a `.Contracts` query/command through `ISender` (in-process,
> same transaction scope available). There is **no gRPC** between modules: they share one process. gRPC (or HTTP) only
> becomes relevant for a module that is physically extracted into its own service - its `.Contracts` are the seam.

---

## 1. Which kind of event?

| | Domain event | Integration event |
|---|---|---|
| Interface | `IDomainEvent` (`Shared.Contracts.Events`, a MediatR `INotification`) | `IIntegrationEvent` / `IntegrationEvent` (`Shared.Contracts.Messaging`) |
| Raised with | `AddDomainEvent(...)` in the aggregate | `AddIntegrationEvent(...)` in the aggregate |
| Delivered by | MediatR, in-process, after the unit of work commits | Transactional outbox → MassTransit bus (InMemory / PostgreSql / RabbitMq) |
| Guarantee | Runs once per committed unit of work; a handler failure fails the caller | **At-least-once**, retried, survives restarts (durable transport); never fails the publisher |
| Consumers | `INotificationHandler<T>` (same module, or another module for in-process contract notifications) | `IConsumer<T>` in the reacting module |
| Lives in | `Domains/<Aggregate>/Events/` (module) - or `.Contracts/.../Events/` when another module handles it in-process | `<Module>.Contracts/<Area>/IntegrationEvents/` |

**Decision rule.** Use an **integration event** for a fact other modules react to *eventually*, where the publisher must
not fail or roll back because of the reaction. If the reaction **must succeed for the command to succeed**, it is not an
event: call the other module synchronously (`ISender` + its `.Contracts`) or, exceptionally, use an in-process contract
notification (a `.Contracts` `IDomainEvent` handled inside the same unit of work).

### Current flows

| Flow | Kind | Why |
|---|---|---|
| Catalog `ProductPublishedIntegrationEvent` → any module (sample consumer logs it) | Integration | cross-module fact; nothing waits for it |
| Subscriptions `SubscriptionActivated/ExpiredIntegrationEvent` → Tenancy `TenantBillingStatusConsumer` (suspend / reactivate tenant) | Integration | billing must never lose the fact, and a Tenancy failure must not break expiry/activation |
| Attachment `UserAvatarChangedIntegrationEvent` → Identity `UserAvatarChangedConsumer` (`ProfilePictureUrl`) | Integration | eventually consistent projection in another module |
| `UserDirectory.Contracts.Events.ImpersonationStartedEvent` → Tenancy audit | **In-process** (sync) | audit-before-token: if the audit write fails, no impersonation token may be issued |
| `TenantDataPurgeRequestedEvent` → Attachment / Notification purge | **In-process** | an orchestration step of `PurgeDeletedTenantsJob`; a failure must stop the purge (the job retries) |
| Tenancy cache invalidation, Subscriptions emails, Catalog logging | Domain (same module) | never leaves the module |

---

## 2. Adding an integration event (checklist)

1. **Contract** in the publishing module's `.Contracts`:
   ```csharp
   // Catalog.Contracts/Products/IntegrationEvents/ProductPublishedIntegrationEvent.cs
   [MessageUrn("catalog.product-published.v1")]           // stable wire name - never change it
   public sealed class ProductPublishedIntegrationEvent : IntegrationEvent
   {
       public Guid ProductId { get; init; }
       public Guid OwnerId { get; init; }
       public string Sku { get; init; } = default!;
       public DateTime PublishedAt { get; init; }
   }
   ```
   - Ids and a minimal projection only - never an aggregate/entity.
   - No `TenantId` property: the tenant travels as a transport header and is restored as the consumer's tenant scope.
2. **Raise it from the aggregate**, next to the domain event:
   ```csharp
   AddDomainEvent(new ProductPublishedEvent(Id, OwnerId, Sku, PublishedAt.Value));
   AddIntegrationEvent(new ProductPublishedIntegrationEvent { ProductId = Id, ... });
   ```
3. **Outbox in the module's DbContext** (once per context):
   - host context: `builder.AddOutbox();` - tenant-data context (`ITenantDataContext`): `builder.AddTenantOutbox();`
     (before `ApplyTenantFilters(builder)`).
   - migration: `dotnet ef migrations add AddOutbox --context <Ctx> --project src/Modules/<M>/<M> --startup-project src/Api --output-dir Data/Migrations`;
     tenant-data contexts add `migrationBuilder.EnableTenantRowLevelSecurity("<schema>", "outbox_messages");`.
   - registration in `<Module>Module.cs`: `services.AddOutbox<TContext>();` (after `AddCustomDbContext`).
4. **Consumer** in the reacting module (its assembly must be passed to `AddMassTransitWithAssemblies(...)`):
   ```csharp
   public sealed class XConsumer(...) : IConsumer<ProductPublishedIntegrationEvent> { ... }
   ```
   Consumer rules:
   - **Idempotent** - the same message may arrive twice (crash after publish, retries). `context.MessageId` = `EventId`.
   - **Order-independent** - treat the event as a *trigger* and re-read the current state from its owner
     (`ISender` + `.Contracts` query) instead of trusting the payload or arrival order
     (see `TenantBillingStatusConsumer`, `UserAvatarChangedConsumer`).
   - Runs **outside MediatR**: if it saves aggregates, dispatch their domain events (`IDomainEventDispatcher.DispatchPendingAsync`
     after `SaveChanges`, or do the work through `IUnitOfWorkRunner`). Enforced by `BackgroundJobEventDispatchTests`.
   - Throw to retry (transient failure); return normally for "nothing to do" (unknown entity, already applied).
5. **Never** call `IPublishEndpoint.Publish` from feature code for cross-module facts - it bypasses the outbox (lost on
   crash, published even when the transaction rolls back).

**Versioning.** Evolve by adding optional properties only. A breaking change is a **new type with a new `.vN` urn**;
publish both while consumers migrate, then delete the old one once its outbox rows and queues are drained.

Architecture tests (`Api.Tests/Architecture/MessagingArchitectureTests`) enforce: events live in `.Contracts`, carry a
unique `[MessageUrn]`, are not also `INotification`s, consumers live in module implementations, `.Contracts` stay thin.

---

## 3. The transactional outbox (how it works)

Code: `src/Shared/Shared.Core/Messaging/Outbox/`.

```
Command handler / job                           same SaveChanges = same DB transaction
  aggregate.AddIntegrationEvent(e) ─────► OutboxInterceptor ─► INSERT <schema>.outbox_messages (urn, json, tenant, trace)
                                              │
                                              └─► stages OutboxMessagesSaved in IDomainEventCollector
                                                  (discarded if the save fails / the transaction rolls back)

after the outermost unit of work commits:
  DomainEventDispatcher ─► OutboxMessagesSavedHandler ─► OutboxRelay.DeliverAsync(ids)   (immediate, best effort)

every minute (Hangfire, Messaging:Outbox:SweepCron):
  OutboxSweepJob ─► OutboxRelay.DeliverAsync(all due rows) + purge delivered rows past retention   (safety net)
```

- **Atomicity.** The row is inserted by the same `SaveChanges` as the aggregate: committed together or not at all.
- **Claiming.** The relay leases rows with `UPDATE ... SET lock_token, locked_until WHERE id IN (...) AND lease free`.
  The lease predicate is on the target row, so concurrent relays (other API instances, immediate vs. sweep) never own
  the same row. A lease that expires (crashed relay) is reclaimed.
- **Publishing.** Each message is published inside **its own tenant scope** (restored from the row), so the tenant
  publish filter stamps `X-Tenant-Id` and the consumer runs as that tenant. `MessageId` = `EventId`.
  The publish span (`StarterKit.Outbox` activity source) is parented on the trace that raised the event.
- **Failures.** `attempts + 1`, `last_error`, `next_attempt_at` = 5s, 10s, 20s ... (max 1h). After
  `Messaging:Outbox:MaxAttempts` (10) the row is **dead-lettered** (`dead_lettered_at`). Permanent errors (unknown urn,
  unreadable payload, tenant no longer exists) are dead-lettered immediately.
- **Tenant data.** Tenant-data contexts use `TenantOutboxMessage` (tenant-owned: named tenant filter, RLS, moved /
  exported / purged with the tenant). The sweep visits them per serviceable tenant (`ITenantJobRunner`); a `Migrating`
  tenant's rows wait until it is serviceable again.
- **Ordering** is best-effort (`occurred_on`); a retried row can overtake a newer one - hence the consumer rules above.

### Configuration (`Messaging:Outbox`)

| Key | Default | Meaning |
|---|---|---|
| `BatchSize` | 100 | rows claimed per relay round |
| `MaxAttempts` | 10 | attempts before dead-lettering |
| `LeaseSeconds` | 60 | claim lease |
| `SweepCron` | `* * * * *` | Hangfire cron of the safety-net sweep |
| `RetentionDays` | 7 | delivered rows are deleted after this; dead letters are kept |

### Operations

```sql
-- Backlog / health per module (schema = catalog, attachment, subscriptions, ...)
SELECT count(*) FILTER (WHERE processed_at IS NULL AND dead_lettered_at IS NULL) AS pending,
       count(*) FILTER (WHERE dead_lettered_at IS NOT NULL)                       AS dead_lettered,
       min(occurred_on) FILTER (WHERE processed_at IS NULL)                       AS oldest_pending
FROM catalog.outbox_messages;

-- Inspect dead letters
SELECT id, message_type, attempts, last_error, occurred_on FROM catalog.outbox_messages
WHERE dead_lettered_at IS NOT NULL ORDER BY occurred_on;

-- Requeue after fixing the cause (picked up by the next sweep). Tenant tables have RLS: run as a role that
-- bypasses it, or set app.bypass_rls = 'on' for the session.
UPDATE catalog.outbox_messages
SET dead_lettered_at = NULL, attempts = 0, next_attempt_at = now(), last_error = NULL
WHERE id = '<id>';
```

Logs: `Outbox message {MessageId} ({MessageType}) DEAD-LETTERED ...` (error) and `... publish failed ... retry at ...`
(warning). Alert on the error.

---

## 4. Transports (`Messaging:Transport`)

| Value | Durable | Infrastructure | Use for |
|---|---|---|---|
| `InMemory` (default) | **No** - messages in flight / being retried are lost on restart; each instance has its own bus | none | development, tests |
| `PostgreSql` | Yes | none new - queues live in the `transport` schema of the existing database (MassTransit SQL transport) | production default for most deployments |
| `RabbitMq` | Yes | a RabbitMQ broker | high throughput, fan-out/routing, non-.NET consumers |

The outbox makes **publishing** reliable with any transport; a durable transport makes **delivery to consumers**
reliable (queued messages and retries survive restarts, failed messages land in `<endpoint>_error` queues).

All transports share the same bus configuration (`Shared.Messaging/Extensions/MassTransitExtensions.cs`): kebab-case
endpoints, tenant publish/send/consume filters, an outer retry for `TenantMigratingException` (~30 min) and an inner
retry for transient failures (5 attempts, exponential).

```jsonc
"Messaging": {
  "Transport": "InMemory",                       // InMemory | PostgreSql | RabbitMq
  "PostgreSql": { "Schema": "transport", "Role": "transport", "CreateInfrastructure": true,
                  "AdminUsername": null, "AdminPassword": null },
  "RabbitMq": { "Host": "localhost", "Port": 5672, "VirtualHost": "/", "Username": "starterkit", "Password": "CHANGE_ME" }
}
```

- **PostgreSql** uses `ConnectionStrings:starterkit`. `CreateInfrastructure: true` creates/upgrades the transport
  schema, role and functions at startup - that needs DDL and `CREATEROLE` rights. In production either give those to
  `AdminUsername`/`AdminPassword` (migration only) or run once with them and set `CreateInfrastructure: false`.
- **RabbitMq** uses `ConnectionStrings:rabbitmq` (an `amqp://user:pass@host:port/vhost` URI - what Aspire injects) when
  present, otherwise `Messaging:RabbitMq:*`. The password is a secret: Docker secret `rabbitmq_password` or env
  `Messaging__RabbitMq__Password` - never in `appsettings*.json`.

Running it:

| Environment | How |
|---|---|
| Aspire | AppHost config/env `Messaging__Transport=RabbitMq` adds a RabbitMQ container (+ management UI) and wires `ConnectionStrings:rabbitmq`; `PostgreSql` needs nothing extra |
| Docker Compose | `MESSAGING_TRANSPORT=PostgreSql` in `.env`, or the RabbitMQ overlay: `docker compose -f docker-compose.yml -f docker-compose.rabbitmq.yml up -d` (add `-f docker-compose.override.yml` in development) |

Sagas: none exist yet and the saga repository is in-memory. The first real saga must use a durable (EF Core) saga
repository.

---

## 5. Testing

- Aggregates: assert `aggregate.IntegrationEvents` after the domain method (pure unit test).
- Persistence: Testcontainers Postgres - after saving, assert the `outbox_messages` row (urn, payload, tenant).
- Consumers: unit-test `Consume` with a mocked `ISender` / real `DbContext`; cover duplicate and out-of-order delivery.
- End-to-end bus behavior: MassTransit test harness (`AddMassTransitTestHarness`).
