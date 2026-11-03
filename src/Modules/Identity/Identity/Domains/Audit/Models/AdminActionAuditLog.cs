namespace Identity.Domains.Audit.Models;

/// <summary>
/// Append-only record of a privileged identity action (admin user/role management, 2FA lifecycle).
/// Same shape as the Subscriptions audit, plus request context (IP, user agent, correlation id).
/// <para>
/// Rows are only ever inserted: <see cref="ApplicationIdentityDbContext"/> rejects updates and deletes.
/// <see cref="BeforeJson"/> / <see cref="AfterJson"/> hold sanitized snapshots (see <c>UserAuditSnapshot</c>) and
/// never passwords, hashes, security stamps, authenticator keys, codes or tokens.
/// </para>
/// </summary>
public class AdminActionAuditLog
{
    public const int MaxReasonLength = 1000;
    public const int MaxUserAgentLength = 512;

    public Guid Id { get; private set; }

    /// <summary>Who did it (the admin, or the user themself for self-service 2FA events).</summary>
    public Guid AdminId { get; private set; }

    public string Action { get; private set; } = default!;
    public string TargetType { get; private set; } = default!;
    public Guid? TargetId { get; private set; }
    public string? Reason { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    private AdminActionAuditLog()
    {
    }

    public static AdminActionAuditLog Create(
        Guid adminId,
        string action,
        string targetType,
        Guid? targetId,
        string? reason,
        string? beforeJson,
        string? afterJson,
        string? ipAddress,
        string? userAgent,
        string? correlationId,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);

        return new AdminActionAuditLog
        {
            Id = Guid.NewGuid(),
            AdminId = adminId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Reason = Truncate(reason, MaxReasonLength),
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            IpAddress = Truncate(ipAddress, 45),
            UserAgent = Truncate(userAgent, MaxUserAgentLength),
            CorrelationId = Truncate(correlationId, 100),
            OccurredAt = occurredAt
        };
    }

    private static string? Truncate(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}

/// <summary>Stable action names stored in <see cref="AdminActionAuditLog.Action"/> (filterable; never rename).</summary>
public static class IdentityAuditActions
{
    public const string UserEdited = "user.edited";
    public const string UserLocked = "user.locked";
    public const string UserUnlocked = "user.unlocked";
    public const string UserStatusChanged = "user.status_changed";
    public const string UserDeleted = "user.deleted";
    public const string RoleAssigned = "role.assigned";
    public const string RoleRevoked = "role.revoked";
    public const string AdminPromoted = "admin.promoted";
    public const string AdminDemoted = "admin.demoted";
    public const string AdminCreated = "admin.created";
    public const string AdminUpdated = "admin.updated";
    public const string AdminPasswordChanged = "admin.password_changed";
    public const string TwoFactorEnabled = "2fa.enabled";
    public const string TwoFactorDisabled = "2fa.disabled";
    public const string TwoFactorReset = "2fa.reset";
    public const string TwoFactorRecoveryCodesRegenerated = "2fa.recovery_codes_regenerated";
    public const string TwoFactorRecoveryCodeUsed = "2fa.recovery_code_used";
    public const string ImpersonationStarted = "impersonation.started";
}

public static class IdentityAuditTargets
{
    public const string User = "User";
}
