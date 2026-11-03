using System.Text.Json;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// Adds an <see cref="AdminActionAuditLog"/> row to the current unit of work (saved with the action itself, so the
/// audit and the change commit or roll back together). Before/after objects are serialized to JSON.
/// </summary>
public interface IAdminAuditWriter
{
    void Record(
        string action,
        Guid targetId,
        string targetType,
        Guid subscriberId = default,
        string? reason = null,
        decimal? amount = null,
        string? currency = null,
        object? before = null,
        object? after = null,
        Guid? adminId = null);
}

public sealed class AdminAuditWriter(SubscriptionsDbContext dbContext, IHttpContextService httpContextService)
    : IAdminAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public void Record(
        string action,
        Guid targetId,
        string targetType,
        Guid subscriberId = default,
        string? reason = null,
        decimal? amount = null,
        string? currency = null,
        object? before = null,
        object? after = null,
        Guid? adminId = null)
    {
        var actor = adminId ?? httpContextService.GetUserIdAsGuid() ?? Guid.Empty;

        dbContext.AdminAuditLogs.Add(AdminActionAuditLog.Create(
            actor,
            action,
            targetId,
            targetType,
            subscriberId,
            reason,
            amount,
            currency,
            Serialize(before),
            Serialize(after)));
    }

    private static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, value.GetType(), JsonOptions);
}
