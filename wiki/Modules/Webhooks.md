# Webhooks Module

Outbound webhooks: tenants register HTTPS endpoints and receive signed, retried deliveries of the platform's public
integration events. Design rationale and the security model: [ADR-0009](../Decisions/0009-outbound-webhooks.md).

Data plane (`ITenantDataContext`, schema `webhooks`): every row carries `tenant_id` and has forced RLS.

## Layout

```
Webhooks.Contracts/   commands, queries, DTOs, WebhookDeliveryStatus
Webhooks/
  WebhooksModule.cs   Add/UseWebhooksModule, options guard, recurring jobs
  Data/               WebhooksDbContext, configurations, repositories, migrations (InitialCreate)
  Domains/Endpoints/  WebhookEndpoint aggregate, auto-disable event + owner email handler, CRUD/rotate/ping handlers, routes
  Domains/Deliveries/ WebhookDelivery, forwarding consumers, DeliverWebhookJob, sweep + purge jobs, log handlers, routes
  Security/           NetworkAddressPolicy, WebhookUrlPolicy, GuardedConnector (SSRF), WebhookHttpSender, WebhookSigner, secret protector
  Services/           event catalog, payload factory, fan-out
Webhooks.Tests/            unit tests (SSRF policy, signer, domain, validators, catalog)
Webhooks.IntegrationTests/ Testcontainers Postgres, real module + RLS app role, real sockets
```

## Routes

All under `api/v1/webhooks`, tenant required, permission `webhooks.manage` (`TenantPermissions.WebhooksManage`; held by the
tenant Admin role and the owner). Create, delete and rotate-secret also forbid impersonation.

| Method | Route | Purpose |
|---|---|---|
| GET | `/event-types` | public event catalog (`*` subscribes to all) |
| GET / POST | `/endpoints` | list / create (returns the `whsec_` secret once) |
| GET / PUT / DELETE | `/endpoints/{id}` | read / update (`enabled:true` re-enables, resets failures) / delete |
| POST | `/endpoints/{id}/rotate-secret` | new secret, shown once |
| POST | `/endpoints/{id}/ping` | queue a signed `webhook.ping` |
| GET | `/deliveries` | log; filters `endpointId`, `status`, `eventType` |
| GET | `/deliveries/{id}` | detail with payload and last response snippet |
| POST | `/deliveries/{id}/redeliver` | new delivery, same `webhook-id` |

## Public events

`catalog.product-published.v1`, `subscriptions.subscription-activated.v1`, `subscriptions.subscription-expired.v1`.
To add one: mark the integration event `[PublicWebhookEvent(Description = "...")]` and add a sealed
`<X>WebhookConsumer : WebhookForwardingConsumer<TEvent>` in `WebhookForwardingConsumer.cs`.

Payload: `{ "id": "evt_<guid>", "type": "...", "timestamp": "...", "tenantId": "...", "data": { ... } }`.

## Verifying a delivery (receiver side)

```
signed  = "{webhook-id}.{webhook-timestamp}.{raw body}"
key     = base64decode(secret without "whsec_")
expect  = "v1," + base64(HMAC_SHA256(key, signed))
```
Compare in constant time, reject timestamps older than 5 minutes, de-duplicate on `webhook-id`.
`WebhookSigner.Verify` is the reference implementation.

## Retries and health

Attempts: 8, delays 10 s, 1 min, 6 min, 36 min, 3.6 h, 6 h, 6 h. A failed attempt counts against the endpoint; at 10
consecutive failures it is disabled and the tenant owner gets the `WebhookEndpointDisabled` email. Deliveries that
exhausted their attempts show as `Failed` and can be redelivered.

## Jobs

| Job id | Class | Schedule |
|---|---|---|
| (enqueued) | `DeliverWebhookJob` | per delivery attempt |
| `webhooks-dispatch-due` | `DispatchDueWebhooksJob` | every minute; re-enqueues deliveries overdue by `SweepStuckAfterMinutes` |
| `webhooks-purge-deliveries` | `PurgeWebhookDeliveriesJob` | `30 3 * * *`; deletes finished deliveries older than `RetentionDays` |

## Configuration (`Webhooks` section)

`MaxAttempts`, `RetryBaseDelaySeconds`, `RetryBackoffFactor`, `MaxRetryDelayMinutes`, `AutoDisableAfterConsecutiveFailures`,
`MaxEndpointsPerTenant`, `RequestTimeoutSeconds`, `MaxResponseBytes`, `MaxPayloadBytes`, `RequireHttps`,
`AllowPrivateNetworks` (development only; startup fails elsewhere), `DispatchSweepCron`, `SweepStuckAfterMinutes`,
`SweepBatchSize`, `RetentionDays`, `PurgeBatchSize`, `PurgeCron`.

## Wiring checklist (done)

`StarterKit.slnx`, `WebhooksModuleManifest` (discovered by the host, ADR-0019), `TenantIsolationArchitectureTests`, `appsettings.json`, `TenantPermissions.WebhooksManage`,
`EmailTemplate.WebhookEndpointDisabled`.
