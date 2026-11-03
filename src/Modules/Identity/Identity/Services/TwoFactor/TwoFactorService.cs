using Identity.Options;
using Microsoft.Extensions.Options;

namespace Identity.Services.TwoFactor;

/// <summary>
/// Thin wrapper over ASP.NET Identity's authenticator support (key storage via <see cref="ProtectedUserStore"/>,
/// RFC 6238 validation via the built-in <see cref="AuthenticatorTokenProvider{TUser}"/>, recovery codes via
/// UserManager). No custom crypto lives here.
/// </summary>
public interface ITwoFactorService
{
    /// <summary>Generates and stores a new (pending) authenticator key. Does not change TwoFactorEnabled or the stamp.</summary>
    Task<string> CreatePendingKeyAsync(ApplicationUser user);

    Task<string?> GetKeyAsync(ApplicationUser user);

    /// <summary>
    /// Validates a 6-digit authenticator code (spaces/dashes ignored) and consumes it: the same valid code is
    /// rejected if used again by the same user within <see cref="TotpReplayGuard.Window"/> (<see cref="ITotpReplayGuard"/>).
    /// Does not touch the failure counter.
    /// </summary>
    Task<bool> VerifyAuthenticatorCodeAsync(ApplicationUser user, string? code);

    /// <summary>
    /// Second-factor check with lockout: an authenticator code, or (when <paramref name="allowRecoveryCode"/>) a
    /// single-use recovery code. A miss increments AccessFailedCount (locks out after Lockout:MaxFailedAccessAttempts),
    /// a hit resets it. Throws <see cref="UnauthorizedException"/> when the account is (or becomes) locked out.
    /// </summary>
    Task<SecondFactorResult> VerifySecondFactorAsync(
        ApplicationUser user, string? code, string? recoveryCode, bool allowRecoveryCode);

    Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(ApplicationUser user);

    /// <summary>Turns 2FA off and deletes the authenticator key and every recovery code.</summary>
    Task RemoveAsync(ApplicationUser user);

    string FormatKey(string key);

    string BuildAuthenticatorUri(ApplicationUser user, string key);
}

public enum SecondFactorResult
{
    Failed,
    AuthenticatorCode,
    RecoveryCode
}

public sealed class TwoFactorService(
    UserManager<ApplicationUser> userManager,
    IUserStore<ApplicationUser> userStore,
    IOptionsMonitor<TwoFactorOptions> options,
    ITotpReplayGuard replayGuard,
    ILogger<TwoFactorService> logger) : ITwoFactorService
{
    public const string LockedOutMessage = "Account is locked out. Please try again later.";

    public async Task<string> CreatePendingKeyAsync(ApplicationUser user)
    {
        if (userStore is not IUserAuthenticatorKeyStore<ApplicationUser> keyStore)
        {
            throw new InvalidOperationException("The user store does not support authenticator keys.");
        }

        var key = userManager.GenerateNewAuthenticatorKey();
        await keyStore.SetAuthenticatorKeyAsync(user, key, CancellationToken.None);
        EnsureSucceeded(await userManager.UpdateAsync(user), "store the authenticator key");
        return key;
    }

    public Task<string?> GetKeyAsync(ApplicationUser user) => userManager.GetAuthenticatorKeyAsync(user);

    public async Task<bool> VerifyAuthenticatorCodeAsync(ApplicationUser user, string? code)
    {
        var normalized = NormalizeCode(code);
        if (normalized is null)
        {
            return false;
        }

        if (!await userManager.VerifyTwoFactorTokenAsync(
                user, userManager.Options.Tokens.AuthenticatorTokenProvider, normalized))
        {
            return false;
        }

        // Valid code: accept it only once per user inside the provider's acceptance window.
        if (await replayGuard.TryConsumeAsync(user.Id, normalized))
        {
            return true;
        }

        logger.LogWarning("Replayed authenticator code rejected for user {UserId}", user.Id);
        return false;
    }

    public async Task<SecondFactorResult> VerifySecondFactorAsync(
        ApplicationUser user, string? code, string? recoveryCode, bool allowRecoveryCode)
    {
        if (await userManager.IsLockedOutAsync(user))
        {
            throw new UnauthorizedException(LockedOutMessage);
        }

        var result = SecondFactorResult.Failed;
        if (!string.IsNullOrWhiteSpace(code) && await VerifyAuthenticatorCodeAsync(user, code))
        {
            result = SecondFactorResult.AuthenticatorCode;
        }
        else if (allowRecoveryCode && !string.IsNullOrWhiteSpace(recoveryCode) &&
                 (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode.Trim())).Succeeded)
        {
            result = SecondFactorResult.RecoveryCode;
        }

        if (result == SecondFactorResult.Failed)
        {
            await userManager.AccessFailedAsync(user);
            logger.LogWarning("Invalid second factor for user {UserId} ({Failures} failures)",
                user.Id, await userManager.GetAccessFailedCountAsync(user));

            if (await userManager.IsLockedOutAsync(user))
            {
                throw new UnauthorizedException(LockedOutMessage);
            }

            return result;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return result;
    }

    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(ApplicationUser user)
    {
        var count = Math.Clamp(options.CurrentValue.RecoveryCodeCount, 5, 20);
        var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, count)
                    ?? throw new InvalidOperationException("Recovery codes could not be generated.");
        return codes.ToList();
    }

    public async Task RemoveAsync(ApplicationUser user)
    {
        EnsureSucceeded(await userManager.SetTwoFactorEnabledAsync(user, false), "disable two-factor authentication");
        EnsureSucceeded(await userManager.RemoveAuthenticationTokenAsync(
            user, ProtectedUserStore.InternalLoginProvider, ProtectedUserStore.AuthenticatorKeyTokenName),
            "remove the authenticator key");
        EnsureSucceeded(await userManager.RemoveAuthenticationTokenAsync(
            user, ProtectedUserStore.InternalLoginProvider, ProtectedUserStore.RecoveryCodeTokenName),
            "remove the recovery codes");
    }

    public string FormatKey(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0) builder.Append(' ');
            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return builder.ToString().ToLowerInvariant();
    }

    public string BuildAuthenticatorUri(ApplicationUser user, string key)
    {
        var issuer = Uri.EscapeDataString(options.CurrentValue.Issuer);
        var account = Uri.EscapeDataString(user.Email ?? user.UserName ?? user.Id.ToString());
        return $"otpauth://totp/{issuer}:{account}?secret={key}&issuer={issuer}&digits=6";
    }

    /// <summary>Digits only; null when the result is not a plausible 6-digit code.</summary>
    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var digits = code.Replace(" ", string.Empty).Replace("-", string.Empty);
        return digits.Length == 6 && digits.All(char.IsAsciiDigit) ? digits : null;
    }

    private static void EnsureSucceeded(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to {what}: {string.Join(", ", result.Errors.Select(e => e.Code))}");
        }
    }
}
