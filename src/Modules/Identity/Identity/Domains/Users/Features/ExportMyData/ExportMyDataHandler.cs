using Identity.Contracts.Users.Dtos;
using Identity.Contracts.Users.Queries;
using Identity.Domains.Users.Exceptions;

namespace Identity.Domains.Users.Features.ExportMyData;

/// <summary>
/// Personal-data export of the caller (Identity only; other modules can add sections later). Read model straight
/// from the DbContext; contains no secrets (hash, stamp, 2FA keys, tokens).
/// </summary>
public class ExportMyDataHandler(
    UserManager<ApplicationUser> userManager,
    ApplicationIdentityDbContext dbContext,
    TimeProvider timeProvider)
    : IQueryHandler<ExportMyDataQuery, MyDataExportDto>
{
    private const int MaxAuditEntries = 100;

    public async Task<MyDataExportDto> Handle(ExportMyDataQuery query, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(query.UserId.ToString())
                   ?? throw new UserNotFoundException(query.UserId);
        if (!user.IsActive) throw new UnauthorizedException("Account is disabled.");

        var now = timeProvider.GetUtcNow();
        var nowUtc = now.UtcDateTime;
        var roles = await userManager.GetRolesAsync(user);

        var logins = await dbContext.UserLogins.AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .OrderBy(x => x.LoginProvider)
            .Select(x => new MyExportLogin(x.LoginProvider))
            .ToListAsync(cancellationToken);

        var sessions = await dbContext.RefreshTokens.AsNoTracking()
            .Where(x => x.UserId == user.Id && x.RevokedAt == null && x.ExpiresAt > nowUtc)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new MyExportSession(x.CreatedAt, x.ExpiresAt, x.CreatedByIp, x.TenantId, x.MultiFactor))
            .ToListAsync(cancellationToken);

        var audit = await dbContext.AdminAuditLogs.AsNoTracking()
            .Where(x => x.TargetId == user.Id)
            .OrderByDescending(x => x.OccurredAt)
            .Take(MaxAuditEntries)
            .Select(x => new MyExportAuditEntry(x.Action, x.OccurredAt, x.AdminId == user.Id, x.Reason))
            .ToListAsync(cancellationToken);

        return new MyDataExportDto(
            nowUtc,
            new MyExportProfile(user.Id, user.FirstName, user.LastName, user.ProfilePictureUrl,
                user.IsProfileComplete, user.CreatedAt, user.UpdatedAt, user.LastLoginAt),
            new MyExportContact(user.Email, user.EmailConfirmed, user.PhoneNumber, user.IsPhoneNumberVerified),
            roles.ToList(),
            logins,
            new MyExportSecurity(user.TwoFactorEnabled, user.PasswordHash is not null,
                user.LockoutEnd is { } end && end > now),
            sessions,
            audit);
    }
}
