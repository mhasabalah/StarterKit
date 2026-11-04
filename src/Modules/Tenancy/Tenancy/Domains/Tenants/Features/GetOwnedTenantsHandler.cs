namespace Tenancy.Domains.Tenants.Features;

/// <summary>Cross-tenant lookup (host scope): the live tenants a user owns.</summary>
public class GetOwnedTenantsHandler(TenancyDbContext dbContext)
    : IQueryHandler<GetOwnedTenantsQuery, IReadOnlyList<OwnedTenantDto>>
{
    public async Task<IReadOnlyList<OwnedTenantDto>> Handle(GetOwnedTenantsQuery query, CancellationToken cancellationToken)
    {
        using (TenantScope.BeginHostScope())
        {
            return await dbContext.Tenants.AsNoTracking()
                .Where(t => t.OwnerUserId == query.UserId &&
                            t.Status != TenantStatus.Deleted && t.Status != TenantStatus.PendingDeletion)
                .OrderBy(t => t.Identifier)
                .Select(t => new OwnedTenantDto(t.Id, t.Identifier, t.Name))
                .ToListAsync(cancellationToken);
        }
    }
}
