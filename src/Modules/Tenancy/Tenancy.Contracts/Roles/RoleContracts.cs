namespace Tenancy.Contracts.Roles;

public sealed record TenantRoleDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int MemberCount,
    long Version);

public sealed record GetTenantRolesQuery : IQuery<IReadOnlyList<TenantRoleDto>>;

/// <summary>Every grantable tenant permission (for role editors).</summary>
public sealed record GetTenantPermissionsQuery : IQuery<IReadOnlyList<string>>;

public sealed record CreateTenantRoleCommand(string Name, string? Description, List<string> Permissions)
    : ICommand<CreateTenantRoleResult>;

public sealed record CreateTenantRoleResult(Guid Id);

public sealed record UpdateTenantRoleCommand(Guid Id, string Name, string? Description, List<string> Permissions,
    long? Version = null) : ICommand<UpdateTenantRoleResult>;

public sealed record UpdateTenantRoleResult(Guid Id, long Version);

/// <summary>Deletes a custom role that no member holds.</summary>
public sealed record DeleteTenantRoleCommand(Guid Id) : ICommand<DeleteTenantRoleResult>;

public sealed record DeleteTenantRoleResult(Guid Id);

// ----- audit -----

public sealed record TenantAuditLogDto(
    Guid Id,
    Guid? ActorUserId,
    Guid? ImpersonatorUserId,
    string Action,
    string TargetType,
    string? TargetId,
    string? DataJson,
    string? IpAddress,
    string? CorrelationId,
    DateTime OccurredAt);

public sealed record GetTenantAuditLogQuery : BaseQuery, IQuery<PaginatedResult<TenantAuditLogDto>>
{
    public string? Action { get; init; }
    public Guid? ActorUserId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}
