# 0021. The aggregate version covers its child collections

## Status

- **Status:** Accepted
- **Date:** 2026-11-08

## Context

`AuditableEntityInterceptor` bumps `Version` when the root row changes. A method that only adds, removes or edits child
rows leaves the root `Unchanged`, so nothing checks it. Two concurrent requests then both pass an invariant that spans
the children. A concrete case: two admins each remove a different verified domain from an active SSO connection, both see
two verified domains, and the connection ends up with none. A client that holds an old version is not told either.

## Decision

The root is versioned for every change to the aggregate. A behaviour method that changes only children calls
`Aggregate.BumpVersionOnSave()`. It is only a request: the interceptor stays the one place that sets `Version`. A method
that already changes a root column needs nothing, because that column makes the root `Modified`.

| Aggregate | Children | How the root is touched |
|---|---|---|
| `Product` | images, attributes, tags | `UpdatedAt` in every behaviour method |
| `Plan` | features | `UpdatedAt` |
| `Subscription` | payments, scheduled plan changes | `UpdatedAt` in each method that adds or changes them |
| `SubscriptionPayment` | receipts | `AmountReceived` (a child of `Subscription` with its own version; the handler also calls `Subscription.Touch`) |
| `SsoConnection` | domains | `BumpVersionOnSave` |
| `Tenant` | custom domains (owned) | `BumpVersionOnSave` |
| `FeatureFlag` | overrides | `BumpVersionOnSave` |

A new aggregate with children picks one of these two ways. Set-based updates that record bookkeeping
(`SamlMetadataRefreshedAt`, `ViewCount`) stay outside the version on purpose.

## Consequences

- Concurrent child changes on one aggregate conflict: the loser gets 409 and retries.
- **Client-visible:** the version moves after a domain or override change, and those responses return the child, not the
  root. A client that sends `version` must re-read the aggregate first, or it gets 409.
- The audit trail stays quiet: `Version` is a skipped column, so a child-only bump writes no audit row.
- **Enforced by:** `Shared.Core.Tests/Concurrency/ChildCollectionVersionTests` (real PostgreSQL),
  `Tenancy.IntegrationTests/ChildCollectionVersionTests` and `SsoTenantTests`.

## Alternatives considered

- **Bump automatically for any tracked child change:** the interceptor cannot tell an aggregate child from an independent
  row, and it would make high-churn children conflict with unrelated edits. The choice belongs to the aggregate.
- **A dummy `UpdatedAt` column on every aggregate:** a schema change and audit noise to get what one call gives.
- **`SELECT ... FOR UPDATE` on the root:** serializes the writers but does nothing for a stale client version.

## Related

[Concurrency and Versions](../Architecture/Concurrency-and-Versions.md).
