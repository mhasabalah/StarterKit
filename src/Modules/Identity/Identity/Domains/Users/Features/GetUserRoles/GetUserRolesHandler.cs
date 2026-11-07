using UserDirectory.Contracts.Queries;

namespace Identity.Domains.Users.Features.GetUserRoles;

/// <summary><see cref="GetUserRolesQuery"/>: the role names of one user (empty when unknown).</summary>
public sealed class GetUserRolesHandler(ApplicationIdentityDbContext dbContext)
    : IQueryHandler<GetUserRolesQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(GetUserRolesQuery query, CancellationToken cancellationToken) =>
        await dbContext.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == query.UserId)
            .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
            .Where(n => n != null)
            .Select(n => n!)
            .ToListAsync(cancellationToken);
}
