using Identity.Contracts.Scim;
using Identity.Data.Repository.Scim;
using Identity.Domains.Scim.Models;
using Identity.Services.Scim;
using Identity.Services.Sso;
using Shared.Core.MultiTenancy;

namespace Identity.Domains.Scim.Features.ScimDirectories;

public sealed class CreateScimDirectoryCommandValidator : AbstractValidator<CreateScimDirectoryCommand>
{
    public CreateScimDirectoryCommandValidator() =>
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ScimDirectory.NameMaxLength);
}

public sealed class CreateScimDirectoryHandler(
    IScimDirectoryRepository directories,
    ITenantContext tenant,
    ScimDirectoryMapper mapper) : ICommandHandler<CreateScimDirectoryCommand, ScimDirectoryCreatedDto>
{
    public const int MaxDirectoriesPerTenant = 5;

    public async Task<ScimDirectoryCreatedDto> Handle(CreateScimDirectoryCommand command, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new TenantNotResolvedException();
        if (await directories.CountForTenantAsync(tenantId, cancellationToken) >= MaxDirectoriesPerTenant)
            throw new ConflictException($"A tenant can have at most {MaxDirectoriesPerTenant} SCIM directories.");

        var (directory, token) = ScimDirectory.Create(tenantId, command.Name);
        directories.Add(directory);
        await directories.SaveAsync(cancellationToken);
        return new ScimDirectoryCreatedDto(await mapper.ToDtoAsync(directory, cancellationToken), token);
    }
}

public sealed class RotateScimDirectoryTokenHandler(
    IScimDirectoryRepository directories,
    ITenantContext tenant,
    ScimDirectoryMapper mapper) : ICommandHandler<RotateScimDirectoryTokenCommand, ScimDirectoryCreatedDto>
{
    public async Task<ScimDirectoryCreatedDto> Handle(RotateScimDirectoryTokenCommand command, CancellationToken cancellationToken)
    {
        var directory = await ScimDirectoryAccess.LoadAsync(directories, tenant, command.Id, cancellationToken);
        var token = directory.RotateToken();
        await directories.SaveAsync(cancellationToken);
        return new ScimDirectoryCreatedDto(await mapper.ToDtoAsync(directory, cancellationToken), token);
    }
}

public sealed class SetScimDirectoryEnabledHandler(
    IScimDirectoryRepository directories,
    ApplicationIdentityDbContext dbContext,
    ITenantContext tenant,
    ScimDirectoryMapper mapper) : ICommandHandler<SetScimDirectoryEnabledCommand, ScimDirectoryDto>
{
    public async Task<ScimDirectoryDto> Handle(SetScimDirectoryEnabledCommand command, CancellationToken cancellationToken)
    {
        var directory = await ScimDirectoryAccess.LoadAsync(directories, tenant, command.Id, cancellationToken);
        dbContext.ExpectVersion(directory, command.Version);
        directory.SetEnabled(command.Enabled);
        await directories.SaveAsync(cancellationToken);
        return await mapper.ToDtoAsync(directory, cancellationToken);
    }
}

public sealed class DeleteScimDirectoryHandler(
    IScimDirectoryRepository directories,
    ITenantContext tenant) : ICommandHandler<DeleteScimDirectoryCommand, DeleteScimDirectoryResult>
{
    public async Task<DeleteScimDirectoryResult> Handle(DeleteScimDirectoryCommand command, CancellationToken cancellationToken)
    {
        var directory = await ScimDirectoryAccess.LoadAsync(directories, tenant, command.Id, cancellationToken);
        directories.Remove(directory); // users/groups mirror cascades; accounts + memberships stay
        await directories.SaveAsync(cancellationToken);
        return new DeleteScimDirectoryResult(directory.Id);
    }
}

/// <summary>One SQL round trip: the user/group counts are correlated sub-queries, not one query per directory.</summary>
public sealed class GetScimDirectoriesHandler(
    ApplicationIdentityDbContext dbContext,
    ITenantContext tenant,
    ScimProvisioningService scim) : IQueryHandler<GetScimDirectoriesQuery, List<ScimDirectoryDto>>
{
    public async Task<List<ScimDirectoryDto>> Handle(GetScimDirectoriesQuery query, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new TenantNotResolvedException();
        var baseUrl = scim.BaseUrl;
        return await dbContext.ScimDirectories.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .OrderBy(d => d.Name)
            .Take(CreateScimDirectoryHandler.MaxDirectoriesPerTenant * 2) // bounded by the per-tenant cap
            .Select(d => new ScimDirectoryDto(
                d.Id, d.Name, d.Enabled, d.TokenPrefix, baseUrl, d.CreatedAt, d.LastRequestAt,
                dbContext.ScimUsers.Count(u => u.DirectoryId == d.Id),
                dbContext.ScimGroups.Count(g => g.DirectoryId == d.Id),
                d.Version))
            .ToListAsync(cancellationToken);
    }
}

public sealed class GetScimGroupsHandler(
    ApplicationIdentityDbContext dbContext,
    ITenantContext tenant) : IQueryHandler<GetScimGroupsQuery, List<ScimGroupDto>>
{
    public async Task<List<ScimGroupDto>> Handle(GetScimGroupsQuery query, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new TenantNotResolvedException();
        if (!await dbContext.ScimDirectories.AnyAsync(d => d.Id == query.DirectoryId && d.TenantId == tenantId, cancellationToken))
            throw new NotFoundException("SCIM directory", query.DirectoryId);

        return await dbContext.ScimGroups.AsNoTracking()
            .Where(g => g.DirectoryId == query.DirectoryId)
            .OrderBy(g => g.DisplayName)
            .Take(500)
            .Select(g => new ScimGroupDto(g.Id, g.DisplayName, g.ExternalId, g.TenantRoleId, g.Members.Count))
            .ToListAsync(cancellationToken);
    }
}

public sealed class MapScimGroupRoleCommandValidator : AbstractValidator<MapScimGroupRoleCommand>
{
    public MapScimGroupRoleCommandValidator()
    {
        RuleFor(x => x.DirectoryId).NotEmpty();
        RuleFor(x => x.GroupId).NotEmpty();
        RuleFor(x => x.TenantRoleId).NotEqual(Guid.Empty).When(x => x.TenantRoleId.HasValue);
    }
}

/// <summary>
/// Maps a group to a tenant role (validated through Tenancy: it must be a role of THIS tenant) and immediately
/// re-applies roles to the directory users.
/// </summary>
public sealed class MapScimGroupRoleHandler(
    IScimDirectoryRepository directories,
    ITenantContext tenant,
    ITenantRoleGuard roles,
    ScimProvisioningService scim,
    TimeProvider timeProvider) : ICommandHandler<MapScimGroupRoleCommand, ScimGroupDto>
{
    public async Task<ScimGroupDto> Handle(MapScimGroupRoleCommand command, CancellationToken cancellationToken)
    {
        await ScimDirectoryAccess.LoadAsync(directories, tenant, command.DirectoryId, cancellationToken);
        var group = await directories.GetGroupAsync(command.DirectoryId, command.GroupId, cancellationToken)
                    ?? throw new NotFoundException("SCIM group", command.GroupId);

        if (command.TenantRoleId is { } roleId)
            await roles.EnsureTenantRolesAsync([roleId], cancellationToken);

        group.MapRole(command.TenantRoleId, timeProvider.GetUtcNow().UtcDateTime);
        await directories.SaveAsync(cancellationToken);

        // Members of every group are re-evaluated: unmapping the last group would otherwise strand stale roles.
        var members = await directories.GetUserIdsAsync(command.DirectoryId, cancellationToken);
        await scim.SyncRolesAsync(command.DirectoryId, members, cancellationToken);

        return new ScimGroupDto(group.Id, group.DisplayName, group.ExternalId, group.TenantRoleId, group.Members.Count);
    }
}

internal static class ScimDirectoryAccess
{
    public static async Task<ScimDirectory> LoadAsync(
        IScimDirectoryRepository directories, ITenantContext tenant, Guid id, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new TenantNotResolvedException();
        return await directories.GetAsync(tenantId, id, ct) ?? throw new NotFoundException("SCIM directory", id);
    }
}

public sealed class ScimDirectoryMapper(IScimDirectoryRepository directories, ScimProvisioningService scim)
{
    public async Task<ScimDirectoryDto> ToDtoAsync(ScimDirectory d, CancellationToken ct)
    {
        var (users, groups) = await directories.CountResourcesAsync(d.Id, ct);
        return new ScimDirectoryDto(d.Id, d.Name, d.Enabled, d.TokenPrefix, scim.BaseUrl, d.CreatedAt, d.LastRequestAt,
            users, groups, d.Version);
    }
}
