namespace UserDirectory.Contracts.Events;

/// <summary>
/// Raised when a platform user started acting as <see cref="TargetUserId"/> in <see cref="TenantId"/>
/// (Tenancy writes it to the tenant's audit trail). Any identity provider adapter that supports impersonation
/// must publish it.
/// </summary>
public sealed record ImpersonationStartedEvent(
    Guid TenantId,
    Guid ImpersonatorUserId,
    Guid TargetUserId,
    string Reason,
    DateTime ExpiresAt) : IDomainEvent;
