using Identity.Contracts.Users.Dtos;
using Identity.Contracts.Users.Queries;

namespace Identity.Domains.Users.Features.GetUsers;

public class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).GreaterThan(0).LessThanOrEqualTo(100);
    }
}

public class GetUsersHandler(ApplicationIdentityDbContext dbContext)
    : IQueryHandler<GetUsersQuery, PaginatedResult<UserListDto>>
{
    public async Task<PaginatedResult<UserListDto>> Handle(
        GetUsersQuery query,
        CancellationToken cancellationToken)
    {
        var queryable = dbContext.Users.AsNoTracking();

        queryable = ApplyFilters(queryable, query);

        var dtoQuery = queryable.Select(u => new UserListDto
        {
            Id = u.Id,
            UserName = u.UserName!,
            Email = u.Email!,
            PhoneNumber = u.PhoneNumber,
            FullName = (u.FirstName ?? "") + " " + (u.LastName ?? ""),
            ProfilePictureUrl = u.ProfilePictureUrl,
            IsActive = u.IsActive,
            IsLocked = u.LockoutEnabled && u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow,
            EmailConfirmed = u.EmailConfirmed,
            Roles = dbContext.UserRoles
                .Where(ur => ur.UserId == u.Id)
                .Join(dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                .ToList(),
            CreatedAt = u.CreatedAt,
            LastLoginAt = u.LastLoginAt
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
        GetUsersQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var pattern = $"%{query.SearchTerm}%";
            queryable = queryable.Where(u =>
                EF.Functions.ILike(u.UserName!, pattern) ||
                EF.Functions.ILike(u.Email!, pattern) ||
                (u.FirstName != null && EF.Functions.ILike(u.FirstName, pattern)) ||
                (u.LastName != null && EF.Functions.ILike(u.LastName, pattern)) ||
                (u.PhoneNumber != null && EF.Functions.ILike(u.PhoneNumber, pattern)));
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

        if (query.EmailConfirmed.HasValue)
            queryable = queryable.Where(u => u.EmailConfirmed == query.EmailConfirmed.Value);

        if (!string.IsNullOrWhiteSpace(query.Role))
            queryable = queryable.Where(u =>
                dbContext.UserRoles.Any(ur => ur.UserId == u.Id &&
                    dbContext.Roles.Any(r => r.Id == ur.RoleId && r.Name == query.Role)));

        if (!string.IsNullOrWhiteSpace(query.OAuthProvider))
            queryable = queryable.Where(u => u.OAuthProvider == query.OAuthProvider);

        if (query.CreatedFrom.HasValue)
            queryable = queryable.Where(u => u.CreatedAt >= query.CreatedFrom.Value);

        if (query.CreatedTo.HasValue)
            queryable = queryable.Where(u => u.CreatedAt <= query.CreatedTo.Value);

        if (query.LastLoginFrom.HasValue)
            queryable = queryable.Where(u => u.LastLoginAt != null && u.LastLoginAt >= query.LastLoginFrom.Value);

        if (query.LastLoginTo.HasValue)
            queryable = queryable.Where(u => u.LastLoginAt != null && u.LastLoginAt <= query.LastLoginTo.Value);

        return queryable;
    }
}
