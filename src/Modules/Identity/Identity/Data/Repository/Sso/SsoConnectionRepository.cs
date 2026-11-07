using Identity.Contracts.Sso;
using Identity.Domains.Sso.Models;

namespace Identity.Data.Repository.Sso;

/// <summary>SSO connection aggregate access. Tenant-scoped reads take the tenant id explicitly (control-plane data).</summary>
public interface ISsoConnectionRepository
{
    Task<SsoConnection?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Any tenant and status (test round trips, background jobs).</summary>
    Task<SsoConnection?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Any tenant: sign-in endpoints address a connection by id before any tenant is known.</summary>
    Task<SsoConnection?> GetActiveAsync(Guid id, CancellationToken ct = default);

    /// <summary>The active connection that verified <paramref name="domain"/> (at most one platform-wide).</summary>
    Task<SsoConnection?> FindActiveByVerifiedDomainAsync(string domain, CancellationToken ct = default);

    /// <summary>True when another connection already verified <paramref name="domain"/>.</summary>
    Task<bool> IsDomainVerifiedElsewhereAsync(string domain, Guid exceptConnectionId, CancellationToken ct = default);

    /// <summary>
    /// Records the last sign-in WITHOUT loading/saving the aggregate (no version bump, so concurrent sign-ins and admin
    /// edits never conflict). Throttled to once a minute per connection.
    /// </summary>
    Task TouchLastLoginAsync(Guid id, DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Records a metadata refresh that changed nothing (<paramref name="refreshedAt"/>, clears the error) or failed
    /// (<paramref name="error"/> code) WITHOUT loading/saving the aggregate: no version bump, no conflict with an admin edit.
    /// </summary>
    Task RecordMetadataRefreshAsync(Guid id, DateTime? refreshedAt, string? error, CancellationToken ct = default);

    /// <summary>
    /// Claims the certificate expiry alert of <paramref name="thresholdDays"/> for the expiry date
    /// <paramref name="notAfter"/> with ONE conditional set-based update (<c>WHERE alerted_not_after IS DISTINCT FROM
    /// notAfter OR alerted_days IS NULL OR alerted_days &gt; band</c>). True = this caller won the row and must send the
    /// alert; replicas racing on the same alert get false. No version bump (no 409 for a concurrent admin edit).
    /// </summary>
    Task<bool> TryClaimExpiryAlertAsync(Guid id, DateTime notAfter, int thresholdDays, CancellationToken ct = default);

    /// <summary>
    /// Undoes a claim whose delivery failed, so the next run retries (only if the row still holds exactly that claim).
    /// </summary>
    Task ReleaseExpiryAlertAsync(Guid id, DateTime notAfter, int thresholdDays, DateTime? previousNotAfter,
        int? previousDays, CancellationToken ct = default);

    /// <summary>Active SAML connections that have a metadata URL (ids only; background refresh).</summary>
    Task<List<Guid>> GetRefreshableSamlIdsAsync(CancellationToken ct = default);

    /// <summary>Active SAML connections (ids only; certificate expiry alerts).</summary>
    Task<List<Guid>> GetActiveSamlIdsAsync(CancellationToken ct = default);

    void Add(SsoConnection connection);

    void Remove(SsoConnection connection);

    Task SaveAsync(CancellationToken ct = default);
}

public sealed class SsoConnectionRepository(ApplicationIdentityDbContext dbContext) : ISsoConnectionRepository
{
    public Task<SsoConnection?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        dbContext.SsoConnections.Include(c => c.Domains)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);

    public Task<SsoConnection?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        dbContext.SsoConnections.Include(c => c.Domains).FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<SsoConnection?> GetActiveAsync(Guid id, CancellationToken ct = default) =>
        dbContext.SsoConnections.Include(c => c.Domains)
            .FirstOrDefaultAsync(c => c.Id == id && c.Status == SsoConnectionStatus.Active, ct);

    public Task<SsoConnection?> FindActiveByVerifiedDomainAsync(string domain, CancellationToken ct = default) =>
        dbContext.SsoConnections.Include(c => c.Domains)
            .Where(c => c.Status == SsoConnectionStatus.Active &&
                        c.Domains.Any(d => d.Domain == domain && d.VerifiedAt != null))
            .FirstOrDefaultAsync(ct);

    public Task<bool> IsDomainVerifiedElsewhereAsync(string domain, Guid exceptConnectionId, CancellationToken ct = default) =>
        dbContext.SsoDomains.AnyAsync(
            d => d.Domain == domain && d.VerifiedAt != null && d.ConnectionId != exceptConnectionId, ct);

    public Task TouchLastLoginAsync(Guid id, DateTime now, CancellationToken ct = default)
    {
        var threshold = now.AddMinutes(-1);
        return dbContext.SsoConnections
            .Where(c => c.Id == id && (c.LastLoginAt == null || c.LastLoginAt < threshold))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastLoginAt, now), ct);
    }

    public Task RecordMetadataRefreshAsync(Guid id, DateTime? refreshedAt, string? error, CancellationToken ct = default) =>
        refreshedAt is { } at
            ? dbContext.SsoConnections.Where(c => c.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.SamlMetadataRefreshedAt, at)
                    .SetProperty(c => c.SamlMetadataLastError, (string?)null), ct)
            : dbContext.SsoConnections.Where(c => c.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.SamlMetadataLastError, error), ct);

    public async Task<bool> TryClaimExpiryAlertAsync(Guid id, DateTime notAfter, int thresholdDays, CancellationToken ct = default) =>
        await dbContext.SsoConnections
            .Where(c => c.Id == id &&
                        (c.SamlExpiryAlertedNotAfter != notAfter || c.SamlExpiryAlertedDays == null ||
                         c.SamlExpiryAlertedDays > thresholdDays))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.SamlCertificatesNotAfter, notAfter)
                .SetProperty(c => c.SamlExpiryAlertedNotAfter, notAfter)
                .SetProperty(c => c.SamlExpiryAlertedDays, thresholdDays), ct) == 1;

    public Task ReleaseExpiryAlertAsync(Guid id, DateTime notAfter, int thresholdDays, DateTime? previousNotAfter,
        int? previousDays, CancellationToken ct = default) =>
        dbContext.SsoConnections
            .Where(c => c.Id == id && c.SamlExpiryAlertedNotAfter == notAfter && c.SamlExpiryAlertedDays == thresholdDays)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.SamlExpiryAlertedNotAfter, previousNotAfter)
                .SetProperty(c => c.SamlExpiryAlertedDays, previousDays), ct);

    public Task<List<Guid>> GetRefreshableSamlIdsAsync(CancellationToken ct = default) =>
        dbContext.SsoConnections.AsNoTracking()
            .Where(c => c.Protocol == SsoProtocol.Saml && c.Status == SsoConnectionStatus.Active &&
                        c.SamlIdpMetadataUrl != null)
            .OrderBy(c => c.Id).Select(c => c.Id).ToListAsync(ct);

    public Task<List<Guid>> GetActiveSamlIdsAsync(CancellationToken ct = default) =>
        dbContext.SsoConnections.AsNoTracking()
            .Where(c => c.Protocol == SsoProtocol.Saml && c.Status == SsoConnectionStatus.Active)
            .OrderBy(c => c.Id).Select(c => c.Id).ToListAsync(ct);

    public void Add(SsoConnection connection) => dbContext.SsoConnections.Add(connection);

    public void Remove(SsoConnection connection) => dbContext.SsoConnections.Remove(connection);

    public Task SaveAsync(CancellationToken ct = default) => dbContext.SaveChangesAsync(ct);
}
