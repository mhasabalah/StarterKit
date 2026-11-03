namespace Shared.Core.MultiTenancy;

/// <summary>
/// Kernel-level lifecycle event every module may handle WITHOUT referencing the Tenancy module: the tenant's grace
/// period ended and its data must be erased. Each module deletes what it owns outside the databases (e.g. the
/// Attachment module deletes the tenant's blobs under <c>t/{tenantId}/</c>); rows of tenant-data contexts are
/// purged centrally by the Tenancy module (dedicated database dropped, shared rows deleted).
/// Raised by the Tenant aggregate and dispatched AFTER the commit; handlers must be idempotent (the purge job
/// retries until the tenant is marked deleted).
/// </summary>
public sealed record TenantDataPurgeRequestedEvent(Guid TenantId, string Identifier, TenantIsolation Isolation)
    : IDomainEvent;
