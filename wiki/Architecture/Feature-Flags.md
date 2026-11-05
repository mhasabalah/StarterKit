# Feature flags

Runtime switches for dark launches, gradual rollout and kill switches. Decision: [ADR-0008](../Decisions/0008-feature-flags.md).

## Use

```csharp
// Endpoint: 404 while the flag is off (does not replace authorization)
group.MapGet("/reports", ...).RequireFlag("reports-v2");

// Code (IFeatureFlags, Shared.Contracts.FeatureFlags)
if (await flags.IsEnabledAsync("reports-v2", ct)) { ... }
```

Keys are lowercase letters, digits, `.`, `_` or `-`. Configuration defaults (used for flags that are not stored):

```json
{ "FeatureFlags": { "Defaults": { "reports-v2": false } } }
```

## Evaluation order

1. Kill switch on: off.
2. User override.
3. Tenant override.
4. Rollout 1..99: on when the `SHA256(key|tenantId)` bucket (0..99) is below the percentage; 100: on.
5. Otherwise the flag's `Enabled`. A flag that is not stored uses `FeatureFlags:Defaults`, else off.

## Storage and caching

Tenancy host database (`tenancy.feature_flags`, `tenancy.feature_flag_overrides`; an override targets exactly one
tenant or one user). One snapshot is cached in memory and `IDistributedCache`; any change evicts both and broadcasts the
L1 eviction over `ILocalCacheInvalidationBus`. Cache failures never fail a request; if the database is unreadable the
configured defaults apply.

## API

| Route | Auth |
|---|---|
| `GET/POST /api/v1/admin/feature-flags`, `GET/PUT/DELETE .../{key}` | `feature-flags.manage` (SuperAdmin), host only |
| `PUT .../{key}/overrides`, `DELETE .../{key}/overrides/{id}` | same; body has exactly one of `tenantId` / `userId` |
| `GET /api/v1/me/feature-flags` | any authenticated user; `{ "key": bool }` for the current tenant and user |

Updates take an optional `version` (optimistic concurrency, 409 when stale).
