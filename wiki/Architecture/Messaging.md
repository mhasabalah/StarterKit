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
| Interface | `IDomainEvent` (`Shared.Contracts.Events`, an `INotification` (`Shared.Contracts.Mediation`)) | `IIntegrationEvent` / `IntegrationEvent` (`Shared.Contracts.Messaging`) |
| Raised with | `AddDomainEvent(...)` in the aggregate | `AddIntegrationEvent(...)` in the aggregate |
| Delivered by | `IPublisher` (owned mediator), in-process, after the unit of work commits | Transactional outbox → MassTransit bus (InMemory / PostgreSql / RabbitMq) |
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
| Identity `UserSessionsRevokedIntegrationEvent` (`UserDirectory.Contracts`; written to the Identity outbox via a `SessionRevocation` tombstone after any session revoke / logout-all / lock / delete / refresh reuse) → Notification `UserSessionsRevokedConsumer` (closes the revoked sessions' hub sockets on every replica) | Integration | live connections must drop within seconds, and a Notification problem must not fail a sign-out |
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
4. **Consumer** in the reacting module (registered automatically: the module manifest's `ConsumerAssemblies`, ADR-0019):
   ```csharp
   public sealed class XConsumer(...) : IConsumer<ProductPublishedIntegrationEvent> { ... }
   ```
   Consumer rules:
   - **Idempotent** - the same message may arrive twice (crash after publish, retries). `context.MessageId` = `EventId`.
   - **Order-independent** - treat the event as a *trigger* and re-read the current state from its owner
     (`ISender` + `.Contracts` query) instead of trusting the payload or arrival order
     (see `TenantBillingStatusConsumer`, `UserAvatarChangedConsumer`).
   - Runs **outside the mediator**: if it saves aggregates, dispatch their domain events (`IDomainEventDispatcher.DispatchPendingAsync`
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

**Metrics.** `outbox.messages.pending` and `outbox.messages.dead_lettered` (gauges, tag `context` = DbContext name, e.g.
`CatalogDbContext`; Prometheus: `outbox_messages_pending`, `outbox_messages_dead_lettered`). The outbox sweep refreshes
cached values each run (`SweepCron`); the metrics callback never queries the database. Tenant-data contexts are summed over
every serviceable tenant, dedicated databases included. A value not refreshed for 10 minutes (the instance no longer runs
the sweep, Hangfire picks one server per run) is no longer reported. Alerts: `infra/observability/configs/alerts.yaml`
(`OutboxDeadLetters`: dead-lettered > 0 for 5m; `OutboxBacklogGrowing`: pending > 10 and rising for 15m).

**Endpoints** (permission `outbox.manage`, SuperAdmin only; requeue is `.DisallowImpersonation()`). `{context}` is the
DbContext type name, case-insensitive. For tenant-data contexts an optional `?tenantId=` targets one tenant (use it for a
tenant on a DEDICATED database); without it the call runs in a small host scope over the shared database.

| Method | Route | Notes |
|---|---|---|
| GET | `/api/v1/admin/outbox/{context}/dead-letters?page=&pageSize=` | Paged `OutboxDeadLetterDto` (id, type, tenant, attempts, `lastError`; no payload) |
| POST | `/api/v1/admin/outbox/{context}/dead-letters/{id}/requeue` | 404 unknown context/row, 409 when the row is not dead-lettered |
| POST | `/api/v1/admin/outbox/{context}/dead-letters/requeue` | All dead letters of the context (and tenant); returns the count |

Requeue resets attempts and the dead-letter mark; the next sweep redelivers (at-least-once). Fix the cause first (e.g. an
unknown URN needs the event type restored, or the row dead-letters again).

**SQL fallback** (no API access, schema = catalog, attachment, subscriptions, ...):

```sql
SELECT count(*) FILTER (WHERE processed_at IS NULL AND dead_lettered_at IS NULL) AS pending,
       count(*) FILTER (WHERE dead_lettered_at IS NOT NULL)                       AS dead_lettered,
       min(occurred_on) FILTER (WHERE processed_at IS NULL)                       AS oldest_pending
FROM catalog.outbox_messages;

-- Tenant tables have RLS: run as a role that bypasses it, or set app.bypass_rls = 'on' for the session.
UPDATE catalog.outbox_messages
SET dead_lettered_at = NULL, attempts = 0, next_attempt_at = now()
WHERE id = '<id>' AND dead_lettered_at IS NOT NULL;
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

All transports share the same bus configuration (`Shared.Messaging/Extensions/MassTransitExtensions.cs`):
- kebab-case endpoints and the tenant publish/send/consume filters;
- **start-up waits for the bus** (`MassTransitHostOptions.WaitUntilStarted`). Without that, a message published
  before the receive endpoints are bound reaches a topic with no subscription: PostgreSql accepts it, routes it to
  nobody, and the outbox marks it delivered, so it is silently lost. Found and pinned by `TenantMessagingBusTests`;
- **`TenantMigratingException` → delayed redelivery** (5s, 30s, 1m, 2m, 5m, 10m, 15m, 30m). The message goes back to the
  transport instead of holding a consumer slot. An in-memory retry let 200 waiting messages of one migrating tenant
  stall every other tenant on the endpoint (`A_migrating_tenant_does_not_block_other_tenants`). InMemory and
  PostgreSql support this natively. RabbitMQ needs the `rabbitmq_delayed_message_exchange` plugin
  (`Messaging:RabbitMq:UseDelayedExchange=true`); without it, RabbitMQ falls back to the old in-memory retry and keeps
  that risk;
- an inner retry for transient failures (5 attempts, exponential). After that the message moves to
  `<endpoint>_error`. The outbox already counts it as delivered, so the **`MessageConsumerFaults`** alert (MassTransit
  `receive.errors`) is what tells you. Fix the consumer, then move the messages back from the `_error` queue (RabbitMQ
  management UI "Move messages", or the `transport` schema for PostgreSql).

```jsonc
"Messaging": {
  "Transport": "InMemory",                       // InMemory | PostgreSql | RabbitMq
  "PostgreSql": { "Schema": "transport", "Role": "transport", "CreateInfrastructure": true,
                  "AdminUsername": null, "AdminPassword": null },
  "RabbitMq": { "Host": "localhost", "Port": 5672, "VirtualHost": "/", "Username": "starterkit", "Password": "CHANGE_ME",
                "UseDelayedExchange": false }          // true once the broker has the delayed-message plugin
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
- End-to-end bus behavior: `Shared.Core.Tests/Messaging/TenantMessagingBusTests` runs the REAL bus configuration on
  InMemory and on PostgreSql (Testcontainers). It covers tenant propagation, host scope, purged-tenant drop,
  migrating-tenant redelivery and noisy-neighbor isolation. These assertions are the contract any future transport or
  bus library must pass.
