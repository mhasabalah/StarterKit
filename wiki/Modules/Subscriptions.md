# Subscriptions module

`src/Modules/Subscriptions` sells plans to **subscribers**. A subscriber is a user in single-tenant mode, or a tenant
when multi-tenancy is on. The module covers plans and features, the subscription lifecycle, manual and proof-based
payments, invoices, refunds, price changes and an append-only money ledger.

| | |
|---|---|
| Schema / context | `subscriptions` / `SubscriptionsDbContext` (host context; maps Postgres 23505 to `ConflictException`; ledger and receipts are append-only) |
| Domains | `Plans` (plans, features, feature definitions, price changes), `Subscriptions` (lifecycle, payments, invoices, refunds, downgrades/upgrades), `Ledger` (billing dashboard, ledger entries), `PaymentSettings` (payment methods, instructions) |
| Publishes | `SubscriptionActivatedIntegrationEvent`, `SubscriptionExpiredIntegrationEvent` (outbox), consumed by Tenancy to suspend or reactivate tenants |
| Notifies | About 20 email templates (activated, renewed, past due, expiring reminder, invoices, refunds, price changes, ...) via `INotificationSender` |
| Permissions | `billing.read/approve/refund/export`, `plans.manage`, `payment-settings.manage` (Finance role); `tenant.billing.manage` inside a tenant |

Billing rules, the ledger, invoicing and the state machine are documented in **[../Subscriptions/Billing.md](../Subscriptions/Billing.md)**.
Payment-method UX for frontends is in [../Subscriptions/PaymentMethods-Frontend-Integration.md](../Subscriptions/PaymentMethods-Frontend-Integration.md).

## HTTP surface (`api/v1/...`)

| Group | Who | Examples |
|---|---|---|
| `plans`, `payment-methods`, `payment-instructions` | Public or signed in | Catalogue of plans and how to pay |
| `subscriptions`, `subscriptions/me[/invoices, /payments, /renewal]` | Subscriber | Subscribe, upload payment proof, upgrade, schedule a downgrade, cancel, request a refund |
| `invoices/{id}/pdf` | Owner or billing staff | Invoice PDF |
| `admin/subscriptions[...]` | `billing.*` | Pending and past-due queues; approve or reject; renewal and proration decisions; extend, comp, cancel, reactivate; manual payments; per-subscriber features |
| `admin/plans[...]`, `admin/feature-definitions` | `plans.manage` | Plan CRUD, features, scheduled or forced price changes |
| `admin/payments`, `admin/refunds`, `admin/invoices` | `billing.*` | Review proofs, refunds, credit notes, resend or retry invoices |
| `admin/payment-methods`, `admin/payment-instructions` | `payment-settings.manage` | Payment configuration |
| `admin/billing/dashboard`, `admin/billing/ledger` | `billing.read` | Finance views |

## Idempotency-Key (money-moving POSTs)

`.WithIdempotency()` (Shared.Core, `Web/Idempotency`) is applied to: create subscription, upload payment / renewal /
proration proof, record manual payment, request refund, upgrade, issue credit note. The `Idempotency-Key` header is
optional; without it nothing changes. With it, scope = tenant + user + method + route template + key.

| Situation | Result |
|---|---|
| First request | runs; a 2xx/4xx response is stored for `Idempotency:Ttl` (default 24 h) |
| Duplicate while the first runs | 409, `errorCode: idempotency_in_progress` |
| Duplicate after completion | stored status + body, header `Idempotency-Replayed: true` |
| Same key, different body | 422, `errorCode: idempotency_key_reused` (multipart bodies are hashed part by part, so a new boundary does not matter) |
| 5xx or exception | key released, a retry runs again |
| Redis unavailable | warning logged, request processed normally |

Reservation is Redis `SET NX` (in-process fallback when `Redis:Enabled=false`). Other settings:
`Idempotency:InFlightTtl` (5 min), `Idempotency:MaxStoredBodyBytes` (256 KB; larger responses are not stored).

## Entitlements (plan features and limits)

`IEntitlementService` (Shared.Core) answers `HasFeatureAsync(key)` / `GetLimitAsync(key)` for the current subscriber
(tenant in multi-tenant mode). Shared.Core registers a permissive default; Subscriptions replaces it with
`PlanEntitlementService`, which reads the active/past-due subscription's snapshotted features and limits through
`EntitlementCache` (`IDistributedCache`, key via `ITenantCacheKeys`, TTL `Subscriptions:Entitlements:CacheSeconds`,
default 60). Status/plan changes (activated, cancelled, expired, past due, rejected, renewed, upgraded, upgrade
reverted, downgrade applied) invalidate it from `EntitlementCacheInvalidationHandler`.

`Subscriptions:Entitlements:Enforce` defaults to `false`: features are allowed and limits read as unlimited, and the
would-be denial is logged at debug level. Set it to `true` to enforce.

Gate an endpoint (not wired to any existing endpoint yet):

```csharp
app.MapGet(EndpointConfig.BaseApiPath + "/reports/advanced", handler)
   .RequireAuthorization()
   .RequireFeature("advanced-reports");   // 403, errorCode "feature_not_in_plan" when the plan lacks it
```

Error responses may now carry a string `errorCode` next to the numeric `code` (`CustomException.ErrorCode`).

## Jobs (Hangfire, recurring)

`ProcessExpiredSubscriptionsJob`, `ProcessPastDueSubscriptionsJob`, `ProcessRenewalRemindersJob`,
`ProcessPlanPriceChangesJob`, `ProcessPriceChangeNotificationsJob`, `ProcessScheduledDowngradeRemindersJob`,
`ProcessUpgradeSlaExpiryJob`, `ProcessInvoiceRetryJob`, `InvoiceDeliveryJob`, `AdminBillingDigestJob`. Their
schedules are in the `Subscriptions` appsettings section. All of them follow the background-job rules
(`IUnitOfWorkRunner`, log and skip on concurrency conflicts).

## Tests

`Subscriptions.Tests` holds domain, handler and job unit tests. `Subscriptions.IntegrationTests` runs against
Testcontainers Postgres: the ledger, unique-index races and lifecycle flows.
