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

## Jobs (Hangfire, recurring)

`ProcessExpiredSubscriptionsJob`, `ProcessPastDueSubscriptionsJob`, `ProcessRenewalRemindersJob`,
`ProcessPlanPriceChangesJob`, `ProcessPriceChangeNotificationsJob`, `ProcessScheduledDowngradeRemindersJob`,
`ProcessUpgradeSlaExpiryJob`, `ProcessInvoiceRetryJob`, `InvoiceDeliveryJob`, `AdminBillingDigestJob`. Their
schedules are in the `Subscriptions` appsettings section. All of them follow the background-job rules
(`IUnitOfWorkRunner`, log and skip on concurrency conflicts).

## Tests

`Subscriptions.Tests` holds domain, handler and job unit tests. `Subscriptions.IntegrationTests` runs against
Testcontainers Postgres: the ledger, unique-index races and lifecycle flows.
