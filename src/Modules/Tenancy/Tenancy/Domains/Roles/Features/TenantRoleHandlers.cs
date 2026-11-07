using Tenancy.Data.Configurations;
using Tenancy.Services;

namespace Tenancy.Domains.Roles.Features;

public class GetTenantRolesHandler(TenancyDbContext dbContext)
    : IQueryHandler<GetTenantRolesQuery, IReadOnlyList<TenantRoleDto>>
{
    public async Task<IReadOnlyList<TenantRoleDto>> Handle(GetTenantRolesQuery query, CancellationToken cancellationToken)
    {
        var roles = await dbContext.Roles.AsNoTracking().OrderBy(r => r.SystemKey == null).ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);
        var counts = await dbContext.Memberships.AsNoTracking()
            .SelectMany(m => m.RoleIds)
            .GroupBy(id => id)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, cancellationToken);

        return roles.Select(r => new TenantRoleDto(r.Id, r.Name, r.Description, r.IsSystem,
                r.EffectivePermissions.Order(StringComparer.Ordinal).ToList(), counts.GetValueOrDefault(r.Id), r.Version))
            .ToList();
    }
}

public class GetTenantPermissionsHandler : IQueryHandler<GetTenantPermissionsQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(GetTenantPermissionsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(TenantPermissions.All);
}

public class CreateTenantRoleCommandValidator : AbstractValidator<CreateTenantRoleCommand>
{
    public CreateTenantRoleCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(TenantRole.NameMaxLength)
            .Must(TenantRole.IsListSafeName).WithMessage("Role names must not contain ';' (it separates roles in member import/export files).");
        RuleFor(x => x.Description).MaximumLength(TenantRole.DescriptionMaxLength);
        RuleFor(x => x.Permissions).NotNull();
        RuleForEach(x => x.Permissions).Must(p => TenantPermissions.All.Contains(p)).WithMessage("Unknown permission '{PropertyValue}'.");
    }
}

public class CreateTenantRoleHandler(TenancyDbContext dbContext, TenancyRequest request, ITenantAuditWriter audit)
    : ICommandHandler<CreateTenantRoleCommand, CreateTenantRoleResult>
{
    public async Task<CreateTenantRoleResult> Handle(CreateTenantRoleCommand command, CancellationToken cancellationToken)
    {
        request.EnsureCanGrant(command.Permissions);
        var role = TenantRole.CreateCustom(request.TenantId, command.Name, command.Description, command.Permissions);
        dbContext.Roles.Add(role);
        audit.Record(TenantAuditActions.RoleCreated, nameof(TenantRole), role.Id.ToString(), command);
        await TenantRoleSaver.SaveAsync(dbContext, role.Name, cancellationToken);
        return new CreateTenantRoleResult(role.Id);
    }
}

public class UpdateTenantRoleCommandValidator : AbstractValidator<UpdateTenantRoleCommand>
{
    public UpdateTenantRoleCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(TenantRole.NameMaxLength)
            .Must(TenantRole.IsListSafeName).WithMessage("Role names must not contain ';' (it separates roles in member import/export files).");
        RuleFor(x => x.Description).MaximumLength(TenantRole.DescriptionMaxLength);
        RuleFor(x => x.Permissions).NotNull();
    }
}

public class UpdateTenantRoleHandler(TenancyDbContext dbContext, TenancyRequest request, ITenantAuditWriter audit)
    : ICommandHandler<UpdateTenantRoleCommand, UpdateTenantRoleResult>
{
    public async Task<UpdateTenantRoleResult> Handle(UpdateTenantRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken)
                   ?? throw new NotFoundException(nameof(TenantRole), command.Id);
        dbContext.ExpectVersion(role, command.Version);

        request.EnsureCanGrant(command.Permissions);
        role.Update(command.Name, command.Description, command.Permissions);
        audit.Record(TenantAuditActions.RoleUpdated, nameof(TenantRole), role.Id.ToString(), command);

        await TenantRoleSaver.SaveAsync(dbContext, role.Name, cancellationToken);
        return new UpdateTenantRoleResult(role.Id, role.Version);
    }
}

public class DeleteTenantRoleHandler(TenancyDbContext dbContext, ITenantAuditWriter audit)
    : ICommandHandler<DeleteTenantRoleCommand, DeleteTenantRoleResult>
{
    public async Task<DeleteTenantRoleResult> Handle(DeleteTenantRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles.FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken)
                   ?? throw new NotFoundException(nameof(TenantRole), command.Id);
        if (role.IsSystem)
            throw new InvalidMembershipException("System roles cannot be deleted.");
        if (await dbContext.Memberships.AnyAsync(m => m.RoleIds.Contains(role.Id), cancellationToken))
            throw new ConflictException("The role is still assigned to members.");

        dbContext.Roles.Remove(role);
        audit.Record(TenantAuditActions.RoleDeleted, nameof(TenantRole), role.Id.ToString(), new { role.Name });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new DeleteTenantRoleResult(role.Id);
    }
}

public class GetTenantAuditLogHandler(TenancyDbContext dbContext)
    : IQueryHandler<GetTenantAuditLogQuery, PaginatedResult<TenantAuditLogDto>>
{
    public Task<PaginatedResult<TenantAuditLogDto>> Handle(GetTenantAuditLogQuery query, CancellationToken cancellationToken)
    {
        var logs = dbContext.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Action))
            logs = logs.Where(a => a.Action == query.Action);
        if (query.ActorUserId is { } actor)
            logs = logs.Where(a => a.ActorUserId == actor);
        // timestamptz only accepts UTC DateTimes: a query-string date binds as Unspecified (taken as UTC).
        if (query.From.AsUtc() is { } fromUtc)
            logs = logs.Where(a => a.OccurredAt >= fromUtc);
        if (query.To.AsUtc() is { } toUtc)
            logs = logs.Where(a => a.OccurredAt <= toUtc);

        return logs.OrderByDescending(a => a.OccurredAt)
            .Select(a => new TenantAuditLogDto(a.Id, a.ActorUserId, a.ImpersonatorUserId, a.Action, a.TargetType,
                a.TargetId, a.DataJson, a.IpAddress, a.CorrelationId, a.OccurredAt))
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}

internal static class TenantRoleSaver
{
    public static async Task SaveAsync(TenancyDbContext dbContext, string name, CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(TenantRoleConfiguration.NameIndexName))
        {
            throw new ConflictException($"A role named '{name}' already exists.");
        }
    }
}
