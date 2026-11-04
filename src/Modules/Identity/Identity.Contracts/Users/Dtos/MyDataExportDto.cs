namespace Identity.Contracts.Users.Dtos;

/// <summary>
/// Personal-data export of the signed-in user (GDPR art. 15/20). Covers the Identity module only; other modules
/// (orders, files, notifications...) can contribute their own sections later. Never contains secrets (password hash,
/// security stamp, 2FA secrets, tokens).
/// </summary>
public sealed record MyDataExportDto(
    DateTime ExportedAt,
    MyExportProfile Profile,
    MyExportContact Contact,
    IReadOnlyList<string> Roles,
    IReadOnlyList<MyExportLogin> ExternalLogins,
    MyExportSecurity Security,
    IReadOnlyList<MyExportSession> ActiveSessions,
    IReadOnlyList<MyExportAuditEntry> RecentAuditEntries);

public sealed record MyExportProfile(
    Guid Id,
    string? FirstName,
    string? LastName,
    string? ProfilePictureUrl,
    bool IsProfileComplete,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? LastLoginAt);

public sealed record MyExportContact(
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberVerified);

public sealed record MyExportLogin(string Provider);

public sealed record MyExportSecurity(bool TwoFactorEnabled, bool HasPassword, bool IsLockedOut);

public sealed record MyExportSession(DateTime CreatedAt, DateTime ExpiresAt, string? IpAddress, Guid? TenantId, bool MultiFactor);

/// <summary>An identity audit entry whose subject is the user.</summary>
public sealed record MyExportAuditEntry(string Action, DateTimeOffset OccurredAt, bool PerformedBySelf, string? Reason);
