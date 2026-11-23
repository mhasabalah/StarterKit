using Identity.Contracts.Admins.Dtos;
using Identity.Contracts.Admins.Queries;

namespace Identity.Domains.Admins.Features.GetAdmins;

public class GetAdminsQueryValidator : AbstractValidator<GetAdminsQuery>
{
    public GetAdminsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(100);
    }
}

public class GetAdminsHandler(ApplicationIdentityDbContext dbContext)
    : IQueryHandler<GetAdminsQuery, PaginatedResult<AdminListDto>>
{
    public async Task<PaginatedResult<AdminListDto>> Handle(
        GetAdminsQuery query,
        CancellationToken cancellationToken)
    {
        var adminRoleIds = new[] { RoleConstants.RoleIds.Admin, RoleConstants.RoleIds.SuperAdmin };

        var queryable = dbContext.Users
            .AsNoTracking()
            .Where(u => dbContext.UserRoles.Any(ur => ur.UserId == u.Id && adminRoleIds.Contains(ur.RoleId)));

        queryable = ApplyFilters(queryable, query);

        var dtoQuery = queryable.Select(u => new AdminListDto
        {
            Id = u.Id,
            UserName = u.UserName!,
            Email = u.Email!,
            FullName = (u.FirstName ?? "") + " " + (u.LastName ?? ""),
            ProfilePictureUrl = u.ProfilePictureUrl,
            Roles = dbContext.UserRoles
                .Where(ur => ur.UserId == u.Id)
                .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                .ToList(),
            IsSuperAdmin = dbContext.UserRoles
                .Any(ur => ur.UserId == u.Id && ur.RoleId == RoleConstants.RoleIds.SuperAdmin),
            IsActive = u.IsActive,
            IsLocked = u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow,
            CreatedAt = u.CreatedAt,
            LastLoginAt = u.LastLoginAt,
            LastAdminActionAt = u.UpdatedAt
        });

        dtoQuery = dtoQuery.OrderByDescending(x => x.CreatedAt);
        dtoQuery = dtoQuery.OrderByDynamic(query.SortColumn, query.SortBy);

        var result = await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);
        result.OrderBy = query.SortColumn;
        result.OrderByDesc = query.SortBy == SortBy.Descending;

        return result;
    }

    private IQueryable<ApplicationUser> ApplyFilters(
        IQueryable<ApplicationUser> queryable,
        GetAdminsQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var pattern = LikePattern.Contains(query.SearchTerm);
            queryable = queryable.Where(u =>
                EF.Functions.ILike(u.UserName!, pattern, LikePattern.EscapeCharacter) ||
                EF.Functions.ILike(u.Email!, pattern, LikePattern.EscapeCharacter) ||
                (u.FirstName != null && EF.Functions.ILike(u.FirstName, pattern, LikePattern.EscapeCharacter)) ||
                (u.LastName != null && EF.Functions.ILike(u.LastName, pattern, LikePattern.EscapeCharacter)));
        }

        if (query.IsActive.HasValue)
            queryable = queryable.Where(u => u.IsActive == query.IsActive.Value);

        if (query.IsLocked.HasValue)
        {
            if (query.IsLocked.Value)
                queryable = queryable.Where(u =>
                    u.LockoutEnabled &&
                    u.LockoutEnd != null &&
                    u.LockoutEnd > DateTimeOffset.UtcNow);
            else
                queryable = queryable.Where(u =>
                    !u.LockoutEnabled ||
                    u.LockoutEnd == null ||
                    u.LockoutEnd <= DateTimeOffset.UtcNow);
        }

        if (query.IsSuperAdmin.HasValue)
        {
            if (query.IsSuperAdmin.Value)
                queryable = queryable.Where(u =>
                    dbContext.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == RoleConstants.RoleIds.SuperAdmin));
            else
                queryable = queryable.Where(u =>
                    !dbContext.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == RoleConstants.RoleIds.SuperAdmin));
        }

        return queryable;
    }
}
