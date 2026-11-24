# System Design

How StarterKit runs as a system: who calls it, which processes and stores it is made of, how modules talk to each other, and what each module touches when a user acts. Start with the big picture, then drill into any module.

## The big picture

![StarterKit at a glance: clients reach Nginx, which proxies to API replicas of one ASP.NET Core image; worker replicas of the same image run Hangfire queues. The modules inside share PostgreSQL (a schema per module), Redis, blob storage and the message transport fed by the transactional outbox, and call email, WhatsApp, push, payment and identity providers.](diagrams/system-big-picture.excalidraw.svg)

*Figure: One image, many modules, one database with a schema per module; work that must not block a request leaves through the outbox or a Hangfire queue.*

Read it from left to right:

1. **Clients** (web, mobile, platform staff, customer systems) reach the deployment only through **Nginx**, which terminates TLS and upgrades hub connections.
2. **API replicas** run the HTTP pipeline, the module endpoints and the real-time hub. They are stateless: any replica serves any request.
3. **Worker replicas** run the same image with only the Hangfire servers moved off the API, so a bulk import never competes with requests.
4. **Modules** own their schema, endpoints and jobs. They call each other synchronously through `ISender` and contracts, or asynchronously through integration events.
5. **Async work** leaves a request in two ways: integration events saved with the change and relayed by the outbox to the message transport, and Hangfire jobs stored in PostgreSQL.
6. **Data** lives in PostgreSQL (row-level security on tenant data), Redis (cache, token blacklist, quotas, the SignalR backplane) and blob storage.
7. **Providers** are reached only through adapters: SMTP, WhatsApp, FCM/APNs, payment gateways, identity providers, bot protection.

## How to read the architecture views

The views below are generated from one architecture model, [wiki/Architecture/model](model/specification.c4), written in [LikeC4](https://likec4.dev). Every view is a projection of the same elements, so a component looks and is named the same everywhere ([ADR-0035](../Decisions/0035-architecture-model-likec4-and-excalidraw-hero.md)).

| Color | Role | Examples |
|---|---|---|
| Blue | HTTP boundary and processes | HTTP pipeline, endpoints, the hub, the app container |
| Purple | Application: modules, handlers, services | Token service, upload pipeline, `INotificationSender` |
| Amber | Infrastructure: adapters and Hangfire jobs | Storage adapters, `DeliverWebhookJob`, Nginx |
| Slate | Stores | PostgreSQL, Redis, blob storage |
| Teal | Events and queues | Message transport, integration event consumers |
| Rose | People and third parties | Users, payment gateways, SMTP |

A solid arrow is a synchronous call. A **dashed teal arrow** is asynchronous: an integration event through the outbox, or a queued job. In the documentation site, click a view (or **Explore**) to zoom, search, open element details, drill into an element that has its own view, and step through dynamic views. To browse the model locally, run `npx likec4 start wiki/Architecture/model`.

## Landscape

```likec4-view
index
```

*Figure: Users and platform staff reach StarterKit through Nginx; StarterKit calls identity, payment, messaging, push and bot-protection providers, and posts webhooks to customer systems.*

## Containers

```likec4-view
containers
```

*Figure: One application image behind Nginx uses PostgreSQL, Redis, the message transport, blob storage and ClamAV, sends telemetry to the observability stack, and is migrated by a deploy-time migrator; providers are on the landscape view.*

| Container | Responsibility | Technology |
|---|---|---|
| Nginx | TLS, routing, WebSocket upgrade, body size limit | Host reverse proxy ([Nginx](../deployment/Nginx.md)) |
| StarterKit app | HTTP pipeline, modules, hub, outbox relay, Hangfire servers | ASP.NET Core 10 ([Program.cs](../../src/Api/Program.cs)) |
| Migrator | Migrations and seeds at deploy, under an advisory lock | The same image with `--migrate-only` |
| PostgreSQL | Module schemas, outbox tables, Hangfire storage, pgvector | PostgreSQL 16 ([Database conventions](DatabaseConventions.md)) |
| Redis | Cache, token blacklist, idempotency keys, quotas, SignalR backplane, eviction pub/sub | Redis 7 ([Redis](../Infrastructure/Redis.md)) |
| Message transport | Integration events from the outbox to consumers | MassTransit on PostgreSQL or RabbitMQ ([Messaging](Messaging.md)) |
| Blob storage | Files and thumbnails under a root per tenant | Local, Azure Blob or S3 ([Storage providers](../Attachment/Storage-Providers.md)) |
| ClamAV | Malware scanning of every upload | clamd ([Attachment security](../Attachment/Security.md)) |
| Observability | Logs, traces, metrics, alerts | Seq, OpenTelemetry, Prometheus, Grafana ([Observability](Observability.md)) |

## Modules and how they talk

```likec4-view
modules
```

*Figure: Modules share the HTTP pipeline, the outbox relay and the Hangfire servers; they call each other synchronously through ISender and contracts, or asynchronously through integration events.*

The choice between the two arrows is a design rule, not a style ([Messaging](Messaging.md)):

- **Synchronous** (`ISender` + the other module's `.Contracts`) when the caller cannot succeed without the answer, for example DataTransfer storing a file through Attachment.
- **Asynchronous** (an integration event through the outbox) when another module reacts eventually and the publisher must never fail because of it, for example Identity announcing a deleted user.

## Production deployment

```likec4-view
production
```

*Figure: Nginx on the host routes to stateless API replicas; worker replicas of the same image run only the Hangfire servers moved off the API; PostgreSQL and Redis sit on the private network, published only on BIND_ADDRESS.*

## Design decisions behind the picture

### The request path

Every request passes the same pipeline in a fixed order: forwarded headers, CORS, error handling, authentication, tenant resolution, authorization, rate limiting, then the endpoint and the mediator behaviors (telemetry, validation, concurrency translation, logging, domain event dispatch). The [request lifecycle](Request-Lifecycle.md) shows each step; the [tenant request view](../Modules/Tenancy.md#system-design) shows it against real components.

### Data ownership and isolation

- One database, **one schema per module**; no module reads another module's tables ([Database conventions](DatabaseConventions.md)).
- Tenant business data carries `tenant_id`, a named query filter and **forced row-level security**, so a missed filter still cannot leak data ([Isolation](../MultiTenancy/Isolation-and-Dedicated-Databases.md)).
- A tenant can move to a **dedicated database**; it is read-only while it migrates.

### Scaling levers

| Pressure | Lever | Where |
|---|---|---|
| More requests | Add API replicas; they hold no state | Docker Compose, Nginx upstream |
| Heavy background work | Move a Hangfire server to worker replicas and scale them | `Hangfire:Servers:<Name>:Enabled`, `docker-compose.worker.yml` |
| Real-time across replicas | SignalR Redis backplane; L1 cache evictions over Redis pub/sub | Notification, Tenancy |
| Event throughput | PostgreSQL transport by default, RabbitMQ when needed | `Messaging:Transport` ([ADR-0022](../Decisions/0022-production-message-transport.md)) |
| A noisy or regulated tenant | Dedicated tenant database | [Tenant operations](../MultiTenancy/Operations.md) |
| Read-heavy lookups | Redis caches with explicit invalidation; projected, no-tracking queries | Module caches |

### Failure posture

| Dependency | When it fails | Behaviour |
|---|---|---|
| Redis | Unreachable | Cache misses fall back to the database; idempotency and quota checks fail open; requests do not fail because of the cache |
| ClamAV | No verdict | Uploads fail closed with 503 `malware_scanner_unavailable` |
| Message transport | Unreachable | Events stay in the outbox; the sweep retries; rows dead-letter after `Messaging:Outbox:MaxAttempts` |
| A consumer | Throws | MassTransit retries; exhausted messages go to `<endpoint>_error` and raise `MessageConsumerFaults` |
| SMTP | Delivery fails | The chain falls back to WhatsApp; every attempt is logged |
| Customer webhook endpoint | Non-2xx or timeout | Retried with back-off; every attempt is in the delivery log |
| Payment provider | Repeats a callback | The inbox stores one row per provider event; settlement runs once |
| A migrating tenant | Being moved | Writes return 503; its jobs and messages are deferred |

### Security boundaries

- TLS ends at Nginx; forwarded headers are trusted only from the proxy.
- Authentication (JWT or API key), then tenant resolution, then authorization: tokens carry roles, permissions are derived per request, and an API key never exceeds its owner.
- Row-level security is the last line for tenant data.
- Calls to customer-supplied URLs (webhooks, identity providers) go through the SSRF-guarded connector; uploads are scanned before they are stored.

## A cross-module flow: erasing a user

```likec4-view
userErasure
```

*Figure: Deleting a user commits the deletion and one outbox row; the integration event then reaches every module that holds data about that user, and each consumer erases its own data independently and idempotently.*

## Module drill-downs

Each module page has a **System design** section: the module's view, one or two flows that start where a user enters, and a table of the runtime components it touches.

| Module | Start where the user enters |
|---|---|
| [Identity](../Identity/README.md#system-design) | Sign-in, refresh and revocation |
| [Tenancy](../Modules/Tenancy.md#system-design) | A tenant request, end to end |
| [Attachment](../Attachment/README.md#system-design) | Uploading a file |
| [Subscriptions](../Modules/Subscriptions.md#system-design) | Paying for a plan online |
| [Notification](../Modules/Notification.md#system-design) | Delivering one notification on every channel |
| [Webhooks](../Modules/Webhooks.md#system-design) | Forwarding an event to a customer endpoint |
| [DataTransfer](../Modules/DataTransfer.md#system-design) | Running a bulk import |
<!--#if (sample)-->
| [Catalog](../Modules/Catalog.md#system-design) | Publishing a product |
<!--#if (agentic)-->
| [Shipping](../Modules/Shipping.md#system-design) | A shipment triggers the preparation agent |
<!--#endif-->
<!--#endif-->
<!--#if (search)-->
| [Search](../Modules/Search.md#system-design) | Rebuilding an index without downtime |
<!--#endif-->
<!--#if (agentic)-->
| [Agentic](../Modules/Agentic.md#system-design) | An agent run with an approval |
<!--#endif-->

## Keeping the model true

- The model is code: change the module's `<module>.c4` file next to [specification.c4](model/specification.c4) in the same pull request that changes the module's stores, jobs, events or providers.
- Name real types. Every element and relationship must be checkable in `src/`.
- A module file holds every edge that mentions that module, so a product generated without an optional module simply has no file for it.
- Validate with `npx likec4 validate wiki/Architecture/model`; the documentation site build fails on an invalid model or an unknown view id. Authoring rules are in the [diagram style guide](../Reference/Diagram-Style.md#which-diagram-tool).
