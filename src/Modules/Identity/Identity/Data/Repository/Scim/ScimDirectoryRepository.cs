using Identity.Domains.Scim.Models;

namespace Identity.Data.Repository.Scim;

/// <summary>
/// SCIM directory aggregate access for the tenant admin commands. Reads take the tenant id explicitly
/// (control-plane data). The SCIM protocol itself works on <see cref="Services.Scim.ScimProvisioningService"/>.
/// </summary>
public interface IScimDirectoryRepository
{
    Task<ScimDirectory?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<int> CountForTenantAsync(Guid tenantId, CancellationToken ct = default);

    Task<ScimGroup?> GetGroupAsync(Guid directoryId, Guid groupId, CancellationToken ct = default);

    /// <summary>Account ids of every user the directory provisioned (for role re-evaluation).</summary>
    Task<List<Guid>> GetUserIdsAsync(Guid directoryId, CancellationToken ct = default);

    Task<(int Users, int Groups)> CountResourcesAsync(Guid directoryId, CancellationToken ct = default);

    void Add(ScimDirectory directory);

    void Remove(ScimDirectory directory);

    Task SaveAsync(CancellationToken ct = default);
}

public sealed class ScimDirectoryRepository(ApplicationIdentityDbContext dbContext) : IScimDirectoryRepository
{
    public Task<ScimDirectory?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        dbContext.ScimDirectories.FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId, ct);

    public Task<int> CountForTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        dbContext.ScimDirectories.CountAsync(d => d.TenantId == tenantId, ct);

    public Task<ScimGroup?> GetGroupAsync(Guid directoryId, Guid groupId, CancellationToken ct = default) =>
        dbContext.ScimGroups.FirstOrDefaultAsync(g => g.DirectoryId == directoryId && g.Id == groupId, ct);

    public Task<List<Guid>> GetUserIdsAsync(Guid directoryId, CancellationToken ct = default) =>
        dbContext.ScimUsers.AsNoTracking().Where(u => u.DirectoryId == directoryId).Select(u => u.UserId).ToListAsync(ct);

    public async Task<(int Users, int Groups)> CountResourcesAsync(Guid directoryId, CancellationToken ct = default)
    {
        var counts = await dbContext.ScimDirectories.AsNoTracking()
            .Where(d => d.Id == directoryId)
            .Select(d => new
            {
                Users = dbContext.ScimUsers.Count(u => u.DirectoryId == d.Id),
                Groups = dbContext.ScimGroups.Count(g => g.DirectoryId == d.Id)
            })
            .FirstOrDefaultAsync(ct);
        return counts is null ? (0, 0) : (counts.Users, counts.Groups);
    }

    public void Add(ScimDirectory directory) => dbContext.ScimDirectories.Add(directory);

    public void Remove(ScimDirectory directory) => dbContext.ScimDirectories.Remove(directory);

    public Task SaveAsync(CancellationToken ct = default) => dbContext.SaveChangesAsync(ct);
}
