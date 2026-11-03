namespace Tenancy.Contracts.Tenants;

// ----- self-service / current tenant -----

/// <summary>
/// Creates a tenant owned by the caller (self-service sign-up or platform admin). Isolation defaults to
/// <c>MultiTenancy:DefaultIsolation</c>; Dedicated/ConnectionSecretRef/Region are honoured for platform admins only.
/// </summary>
public sealed record CreateTenantCommand(
    string Name,
    string Identifier,
    TenantIsolation? Isolation = null,
    string? ConnectionSecretRef = null,
    string? Region = null,
    string? Culture = null,
    string? TimeZone = null,
    Guid? OwnerUserId = null) : ICommand<CreateTenantResult>;

public sealed record CreateTenantResult(Guid TenantId, string Identifier, TenantStatus Status);

public sealed record GetCurrentTenantQuery : IQuery<TenantDto>;

public sealed record UpdateTenantSettingsCommand(
    string Name,
    string? LegalName,
    string? Culture,
    string? TimeZone,
    string? Currency,
    Guid? LogoAttachmentId,
    string? PrimaryColor,
    long? Version = null) : ICommand<UpdateTenantSettingsResult>;

public sealed record UpdateTenantSettingsResult(Guid TenantId);

public sealed record TenantDomainDto(string Host, bool IsVerified, DateTime? VerifiedAt, string RecordName, string RecordValue);

public sealed record GetTenantDomainsQuery : IQuery<IReadOnlyList<TenantDomainDto>>;

public sealed record AddTenantDomainCommand(string Host) : ICommand<TenantDomainDto>;

/// <summary>Checks the DNS TXT record and marks the domain verified when it matches.</summary>
public sealed record VerifyTenantDomainCommand(string Host) : ICommand<TenantDomainDto>;

public sealed record RemoveTenantDomainCommand(string Host) : ICommand<RemoveTenantDomainResult>;

public sealed record RemoveTenantDomainResult(string Host);

/// <summary>Owner only: hands ownership to another ACTIVE member.</summary>
public sealed record TransferTenantOwnershipCommand(Guid NewOwnerUserId) : ICommand<TransferTenantOwnershipResult>;

public sealed record TransferTenantOwnershipResult(Guid TenantId, Guid OwnerUserId);

/// <summary>Owner only: schedules the tenant for deletion after the grace period (cancellable by platform admins).</summary>
public sealed record RequestTenantDeletionCommand(string? Reason) : ICommand<RequestTenantDeletionResult>;

public sealed record RequestTenantDeletionResult(Guid TenantId, DateTime PurgeAfter);

// ----- platform (host) administration -----

public sealed record GetHostTenantsQuery : BaseQuery, IQuery<PaginatedResult<HostTenantDto>>
{
    public string? Search { get; init; }
    public TenantStatus? Status { get; init; }
}

public sealed record SuspendTenantCommand(Guid TenantId, string Reason) : ICommand<TenantStatusResult>;

public sealed record ReactivateTenantCommand(Guid TenantId) : ICommand<TenantStatusResult>;

public sealed record DeleteTenantCommand(Guid TenantId, string? Reason) : ICommand<TenantStatusResult>;

public sealed record CancelTenantDeletionCommand(Guid TenantId) : ICommand<TenantStatusResult>;

/// <summary>
/// Changes where a tenant's data lives WITHOUT copying it (metadata only - for data copied by hand, see
/// wiki/MultiTenancy/Operations.md). The tenant must be suspended. Use <see cref="MoveTenantCommand"/> instead.
/// </summary>
public sealed record ChangeTenantIsolationCommand(Guid TenantId, TenantIsolation Isolation, string? ConnectionSecretRef)
    : ICommand<TenantStatusResult>;

/// <summary>
/// AUTOMATED move of the tenant's data to another database (shared ⇄ dedicated, or another dedicated server):
/// the tenant becomes <c>Migrating</c> (read-only), rows are copied + verified in the background, then the tenant is
/// switched and the source cleaned. Prefer this over <see cref="ChangeTenantIsolationCommand"/>.
/// </summary>
public sealed record MoveTenantCommand(Guid TenantId, TenantIsolation Isolation, string? ConnectionSecretRef)
    : ICommand<TenantStatusResult>;

/// <summary>Re-queues the move of a tenant still <c>Migrating</c> (e.g. after a crash). Idempotent.</summary>
public sealed record RetryTenantMoveCommand(Guid TenantId) : ICommand<TenantStatusResult>;

/// <summary>Gives up a stuck move: the tenant stays where it was (a running copy then fails safely).</summary>
public sealed record AbortTenantMoveCommand(Guid TenantId) : ICommand<TenantStatusResult>;

/// <summary>Re-runs provisioning (dedicated database + seeding) of a tenant whose provisioning failed.</summary>
public sealed record RetryTenantProvisioningCommand(Guid TenantId) : ICommand<TenantStatusResult>;

public sealed record TenantStatusResult(Guid TenantId, TenantStatus Status);

// ----- data portability -----

/// <summary>
/// Owner only: a zip with the tenant's data - control plane as JSON (profile, branches, roles, members,
/// invitations, audit trail) and every tenant-data table as CSV. Audited.
/// </summary>
public sealed record ExportTenantDataCommand : ICommand<TenantDataExportResult>;

/// <summary>The export (a temporary file deleted when <see cref="Content"/> is disposed).</summary>
public sealed record TenantDataExportResult(Stream Content, string FileName);
