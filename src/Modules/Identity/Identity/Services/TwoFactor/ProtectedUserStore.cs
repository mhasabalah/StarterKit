using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Identity.Services.TwoFactor;

/// <summary>
/// The EF user store with two hardened 2FA secrets (everything else is the stock <see cref="UserStore{TUser}"/>):
/// <list type="bullet">
/// <item><b>Authenticator (TOTP) key</b>: stored encrypted with ASP.NET Data Protection (purpose
/// <see cref="AuthenticatorKeyPurpose"/>) instead of plain text in <c>user_tokens</c>. The key ring must be persisted
/// and shared by all instances (Identity persists it to the database, see <c>DataProtectionKeyRepository</c>).</item>
/// <item><b>Recovery codes</b>: only salted SHA-256 hashes are stored (salt = user id), compared in constant time and
/// removed when redeemed (single use). The plain codes exist only in the response that issued them.</item>
/// </list>
/// TOTP generation/validation itself is ASP.NET Identity's <see cref="AuthenticatorTokenProvider{TUser}"/> (RFC 6238).
/// </summary>
public sealed class ProtectedUserStore(
    ApplicationIdentityDbContext context,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<ProtectedUserStore> logger,
    IdentityErrorDescriber? describer = null)
    : UserStore<ApplicationUser, ApplicationRole, ApplicationIdentityDbContext, Guid, IdentityUserClaim<Guid>,
        IdentityUserRole<Guid>, IdentityUserLogin<Guid>, IdentityUserToken<Guid>, IdentityRoleClaim<Guid>,
        IdentityUserPasskey<Guid>>(
        context, describer)
{
    public const string AuthenticatorKeyPurpose = "Identity.TwoFactor.AuthenticatorKey.v1";

    /// <summary>Same login provider / token names ASP.NET Identity uses, so the rows stay where Identity expects them.</summary>
    public const string InternalLoginProvider = "[AspNetUserStore]";
    public const string AuthenticatorKeyTokenName = "AuthenticatorKey";
    public const string RecoveryCodeTokenName = "RecoveryCodes";

    private const char CodeSeparator = ';';

    private readonly IDataProtector _keyProtector = dataProtectionProvider.CreateProtector(AuthenticatorKeyPurpose);

    public override Task SetAuthenticatorKeyAsync(ApplicationUser user, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return SetTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName,
            string.IsNullOrEmpty(key) ? key : _keyProtector.Protect(key), cancellationToken);
    }

    public override async Task<string?> GetAuthenticatorKeyAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var stored = await GetTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, cancellationToken);
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }

        try
        {
            return _keyProtector.Unprotect(stored);
        }
        catch (CryptographicException ex)
        {
            // Lost/rotated-out Data Protection key ring: the user must use a recovery code or have a SuperAdmin
            // reset their 2FA. Never fall back to treating the stored value as a key.
            logger.LogError(ex, "Authenticator key of user {UserId} cannot be decrypted", user.Id);
            return null;
        }
    }

    public override Task ReplaceCodesAsync(
        ApplicationUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(recoveryCodes);

        var hashes = string.Join(CodeSeparator, recoveryCodes.Select(code => HashRecoveryCode(user.Id, code)));
        return SetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, hashes, cancellationToken);
    }

    public override async Task<bool> RedeemCodeAsync(ApplicationUser user, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var hashes = await StoredHashesAsync(user, cancellationToken);
        var candidate = Convert.FromBase64String(HashRecoveryCode(user.Id, code));

        var match = hashes.FirstOrDefault(h =>
            CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(h), candidate));
        if (match is null)
        {
            return false;
        }

        hashes.Remove(match);
        await SetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName,
            string.Join(CodeSeparator, hashes), cancellationToken);
        return true;
    }

    public override async Task<int> CountCodesAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        return (await StoredHashesAsync(user, cancellationToken)).Count;
    }

    /// <summary>Base64 SHA-256 of "userId:CODE" (trimmed, upper-cased). Public for tests.</summary>
    public static string HashRecoveryCode(Guid userId, string code) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{userId:N}:{code.Trim().ToUpperInvariant()}")));

    private async Task<List<string>> StoredHashesAsync(ApplicationUser user, CancellationToken ct)
    {
        var stored = await GetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, ct) ?? string.Empty;
        return stored.Split(CodeSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
