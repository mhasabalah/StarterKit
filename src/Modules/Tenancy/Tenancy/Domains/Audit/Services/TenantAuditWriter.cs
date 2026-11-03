using System.Text.Json;

namespace Tenancy.Domains.Audit.Services;

/// <summary>
/// Adds a <see cref="TenantAuditLog"/> row to the CURRENT unit of work (saved with the handler's SaveChanges, so
/// the audit entry commits atomically with the change it describes).
/// </summary>
public interface ITenantAuditWriter
{
    void Record(string action, string targetType, string? targetId, object? data = null, Guid? tenantId = null);
}

public sealed class TenantAuditWriter(
    TenancyDbContext dbContext,
    ITenantContext tenantContext,
    IHttpContextService httpContextService,
    TimeProvider timeProvider) : ITenantAuditWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(string action, string targetType, string? targetId, object? data = null, Guid? tenantId = null)
    {
        var tenant = tenantId ?? tenantContext.TenantId
            ?? throw new TenantNotResolvedException("audit entries need a tenant.");

        Guid? impersonator = Guid.TryParse(httpContextService.GetClaimValue(TenantClaimTypes.Actor), out var actor)
            ? actor
            : null;

        dbContext.AuditLogs.Add(TenantAuditLog.Create(
            tenant,
            httpContextService.GetUserIdAsGuid(),
            impersonator,
            action,
            targetType,
            targetId,
            data is null ? null : JsonSerializer.Serialize(data, Json),
            httpContextService.GetIpAddress(),
            httpContextService.GetCorrelationId(),
            timeProvider.GetUtcNow().UtcDateTime));
    }
}
