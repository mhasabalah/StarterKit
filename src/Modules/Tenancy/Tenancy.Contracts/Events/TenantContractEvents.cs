namespace Tenancy.Contracts.Events;

// Contract events: other modules may handle them (one event, many handlers). Raised by the Tenant / membership
// aggregates and dispatched after the commit by the domain-event pipeline.

/// <summary>A tenant was created (owner membership, system roles and head office already exist).</summary>
public sealed record TenantCreatedEvent(Guid TenantId, string Identifier, Guid OwnerUserId, TenantIsolation Isolation)
    : IDomainEvent;

/// <summary>Provisioning finished; the tenant serves requests.</summary>
public sealed record TenantActivatedEvent(Guid TenantId) : IDomainEvent;

/// <summary>The tenant was suspended (users blocked or read-only, see MultiTenancy:SuspendedTenantMode).</summary>
public sealed record TenantSuspendedEvent(Guid TenantId, string Reason) : IDomainEvent;

public sealed record TenantReactivatedEvent(Guid TenantId) : IDomainEvent;

/// <summary>Deletion requested: the tenant is unavailable and will be purged after <see cref="PurgeAfter"/>.</summary>
public sealed record TenantDeletionRequestedEvent(Guid TenantId, DateTime PurgeAfter) : IDomainEvent;

/// <summary>Tenant profile/settings/domains/status changed - caches of the tenant snapshot must be dropped.</summary>
public sealed record TenantChangedEvent(Guid TenantId) : IDomainEvent;

/// <summary>A user joined a tenant (owner at creation, accepted invitation).</summary>
public sealed record TenantMemberJoinedEvent(Guid TenantId, Guid UserId) : IDomainEvent;

/// <summary>A membership changed (roles, branches, status, ownership) or was removed - drop cached memberships.</summary>
public sealed record TenantMembershipChangedEvent(Guid TenantId, Guid UserId, bool Removed, bool RolesChanged = false) : IDomainEvent;

/// <summary>Role permissions changed: every membership of the tenant may have new effective permissions.</summary>
public sealed record TenantRolesChangedEvent(Guid TenantId) : IDomainEvent;
