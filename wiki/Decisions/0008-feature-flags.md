# 0008. Own the feature-flag port; store flags in Tenancy

An owned feature-flag port and Tenancy-backed configuration control runtime rollout independently of plan entitlements.

## Status

- **Status:** Accepted
- **Date:** 2026-11-05

## Context

We need to dark-launch, roll out gradually, switch per tenant or user and kill a feature without a deploy. Plan
entitlements (`IEntitlementService`) answer "does the plan include it", not "is it released".

## Decision

- Own port `IFeatureFlags` in `Shared.Contracts` (`IsEnabledAsync`, `GetAllAsync`), consistent with
  [ADR-0007](0007-owned-mediator-port.md): no `Microsoft.FeatureManagement` or vendor SDK in modules.
- Default implementation in `Shared.Core` reads `FeatureFlags:Defaults:<key>` (unknown = off).
- Tenancy replaces it with `TenancyFeatureFlags`: `feature_flags` and `feature_flag_overrides` in the host
  `TenancyDbContext`. Flags are platform data, not tenant-owned.
- Evaluation (`FeatureFlagEvaluator`, pure): kill switch, user override, tenant override, percentage rollout, flag
  `Enabled`; a flag that is not stored uses the configured default.
- Rollout bucket = SHA-256 of `key|tenantId`, first 4 bytes, modulo 100 (never `string.GetHashCode`): stable across
  replicas and restarts, only grows when the percentage rises, independent per flag.
- One cached snapshot (L1 memory + `IDistributedCache`), evicted after every change by a domain-event handler through
  `ILocalCacheInvalidationBus`. Cache failures fall back to the database; a database failure falls back to configured
  defaults and is not cached.
- `.RequireFlag("key")` endpoint filter returns 404 when off. Admin CRUD needs `feature-flags.manage` (SuperAdmin only);
  `GET /api/v1/me/feature-flags` returns the caller's evaluated flags.

## Consequences

- Cheap to evaluate (in-memory dictionary). The snapshot holds all overrides, which is fine for hundreds of flags and
  thousands of overrides; move to per-flag snapshots beyond that.
- Flag changes are visible within the L1 TTL without Redis, immediately with it.
- Rollout is by tenant, so a tenant's users always agree.

## Alternatives considered

- Microsoft.FeatureManagement: ties modules to a package and has no tenant/user override store.
- Config only: needs a deploy per change.
- Per-tenant DB flags: flags are platform decisions and must work before a tenant is resolved.

## Related

[Feature Flags](../Architecture/Feature-Flags.md).
