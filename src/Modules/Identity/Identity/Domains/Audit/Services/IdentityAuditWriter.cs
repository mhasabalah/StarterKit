using System.Text.Json;
using Identity.Domains.Audit.Models;
using Shared.Core.Web;

namespace Identity.Domains.Audit.Services;

/// <summary>
/// Adds an <see cref="AdminActionAuditLog"/> row to the current unit of work. Handlers of <c>IAuditedCommand</c>s call
/// it; <see cref="AuditedCommandTransactionBehavior{TRequest,TResponse}"/> saves the row inside the same transaction
/// as the change. Request context (IP, user agent, correlation id) is captured here, once.
/// <para>Pass only sanitized snapshots (<see cref="UserAuditSnapshot"/>) as before/after - never secrets.</para>
/// </summary>
public interface IIdentityAuditWriter
{
    void Record(
        string action,
        Guid? targetId,
        string targetType = IdentityAuditTargets.User,
        string? reason = null,
        object? before = null,
        object? after = null,
        Guid? actorId = null);

    /// <summary>
    /// Self-service erasure: the user as actor and target, NO snapshot, reason, IP, user agent or correlation id.
    /// The entry itself must not keep personal data of the person who just erased theirs.
    /// </summary>
    void RecordAccountErasure(Guid userId);
}

public sealed class IdentityAuditWriter(
    ApplicationIdentityDbContext dbContext,
    IHttpContextService httpContextService,
    TimeProvider timeProvider) : IIdentityAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(
        string action,
        Guid? targetId,
        string targetType = IdentityAuditTargets.User,
        string? reason = null,
        object? before = null,
        object? after = null,
        Guid? actorId = null)
    {
        var actor = actorId ?? httpContextService.GetUserIdAsGuid() ?? Guid.Empty;

        dbContext.AdminAuditLogs.Add(AdminActionAuditLog.Create(
            actor,
            action,
            targetType,
            targetId,
            reason,
            Serialize(before),
            Serialize(after),
            httpContextService.GetIpAddress(),
            httpContextService.GetUserAgent(),
            httpContextService.GetCorrelationId(),
            timeProvider.GetUtcNow()));
    }

    public void RecordAccountErasure(Guid userId) =>
        dbContext.AdminAuditLogs.Add(AdminActionAuditLog.Create(
            userId, IdentityAuditActions.AccountErased, IdentityAuditTargets.User, userId,
            null, null, null, null, null, null, timeProvider.GetUtcNow()));

    public static string? Serialize(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, value.GetType(), JsonOptions);
}

/// <summary>
/// The ONLY shape of user data written to audit before/after JSON. Contains no password hash, security stamp,
/// authenticator key, recovery codes or tokens - adding such a field here is a bug (a test guards it).
/// </summary>
public sealed record UserAuditSnapshot(
    Guid Id,
    string? UserName,
    string? Email,
    string? PhoneNumber,
    string? FirstName,
    string? LastName,
    bool IsActive,
    DateTimeOffset? LockoutEnd,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles)
{
    public static UserAuditSnapshot Of(ApplicationUser user, IEnumerable<string>? roles = null) => new(
        user.Id,
        user.UserName,
        user.Email,
        user.PhoneNumber,
        user.FirstName,
        user.LastName,
        user.IsActive,
        user.LockoutEnd,
        user.TwoFactorEnabled,
        (roles ?? []).Order(StringComparer.Ordinal).ToList());
}
