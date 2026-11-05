using System.Buffers.Text;
using Identity.Contracts.Passkeys;
using Identity.Data.Repository.RefreshTokens;
using Identity.Domains.Passkeys.Exceptions;
using Identity.Options;
using Identity.Services.Sessions;
using Microsoft.Extensions.Options;

namespace Identity.Services.Passkeys;

/// <summary>
/// WebAuthn ceremonies on top of ASP.NET Core Identity's built-in <see cref="IPasskeyHandler{TUser}"/> (.NET 10).
/// The handler does the cryptography (challenge, origin and RP id, signature, counters, flags). This service adds
/// what a token-based multi-client API needs: server-side single-use ceremony state, step-up for registration,
/// limits and ownership checks. Endpoints and handlers never touch <see cref="IPasskeyHandler{TUser}"/> directly.
/// </summary>
public interface IPasskeyService
{
    Task<PasskeyOptionsResult> BeginRegistrationAsync(ApplicationUser user, CancellationToken ct = default);

    Task<UserPasskeyInfo> CompleteRegistrationAsync(
        ApplicationUser user, string ceremonyId, string credentialJson, string? name, CancellationToken ct = default);

    Task<PasskeyOptionsResult> BeginAssertionAsync(CancellationToken ct = default);

    /// <summary>Verifies the assertion, persists the new signature counter and backup state, returns the owner.</summary>
    Task<(ApplicationUser User, UserPasskeyInfo Passkey)> CompleteAssertionAsync(
        string ceremonyId, string credentialJson, CancellationToken ct = default);

    /// <summary>403 <c>reauthentication_required</c> unless the caller's session signed in within FreshAuthMinutes.</summary>
    Task EnsureFreshSessionAsync(Guid userId, CancellationToken ct = default);

    void EnsureEnabled();
}

public sealed class PasskeyService(
    IPasskeyHandler<ApplicationUser> handler,
    UserManager<ApplicationUser> userManager,
    IPasskeyCeremonyStore ceremonies,
    ISessionManager sessions,
    IRefreshTokenRepository refreshTokens,
    IHttpContextAccessor httpContextAccessor,
    IOptions<PasskeyOptions> options,
    TimeProvider timeProvider,
    ILogger<PasskeyService> logger) : IPasskeyService
{
    public static string InvalidCeremonyMessage => Identity.Resources.IdentityText.Get("Passkey.InvalidCeremony");
    public static string VerificationFailedMessage => Identity.Resources.IdentityText.Get("Passkey.VerificationFailed");

    private readonly PasskeyOptions _options = options.Value;

    private HttpContext Http => httpContextAccessor.HttpContext
                                ?? throw new InvalidOperationException("Passkey ceremonies need an HTTP request.");

    private TimeSpan CeremonyLifetime => TimeSpan.FromMinutes(Math.Clamp(_options.CeremonyTimeoutMinutes, 1, 15));

    public void EnsureEnabled()
    {
        if (!_options.Enabled)
            throw new PasskeysDisabledException();
    }

    public async Task<PasskeyOptionsResult> BeginRegistrationAsync(ApplicationUser user, CancellationToken ct = default)
    {
        EnsureEnabled();

        var existing = await userManager.GetPasskeysAsync(user);
        if (existing.Count >= _options.MaxPasskeysPerUser)
            throw new PasskeyLimitReachedException(_options.MaxPasskeysPerUser);

        // The account name the authenticator shows in its picker: never the placeholder email of a phone account.
        var accountName = HasRealEmail(user) ? user.Email! : user.PhoneNumber ?? user.UserName ?? user.Id.ToString();
        var displayName = string.IsNullOrWhiteSpace(user.FullName) ? accountName : user.FullName;

        var entity = new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = accountName,
            DisplayName = displayName
        };

        var created = await handler.MakeCreationOptionsAsync(entity, Http);
        var ceremonyId = await ceremonies.CreateAsync(
            new PasskeyCeremony(PasskeyCeremonyKind.Registration, user.Id, created.AttestationState ?? string.Empty),
            CeremonyLifetime, ct);

        return new PasskeyOptionsResult(ceremonyId, created.CreationOptionsJson, ExpiresAt());
    }

    public async Task<UserPasskeyInfo> CompleteRegistrationAsync(
        ApplicationUser user, string ceremonyId, string credentialJson, string? name, CancellationToken ct = default)
    {
        EnsureEnabled();

        var ceremony = await ceremonies.ConsumeAsync(ceremonyId, PasskeyCeremonyKind.Registration, ct);

        // A ceremony started by another account can never finish here (same message: nothing to probe).
        if (ceremony is null || ceremony.UserId != user.Id)
            throw new InvalidPasskeyException(InvalidCeremonyMessage);

        var result = await handler.PerformAttestationAsync(new PasskeyAttestationContext
        {
            HttpContext = Http,
            CredentialJson = credentialJson,
            AttestationState = ceremony.State
        });

        if (!result.Succeeded)
        {
            logger.LogWarning("Passkey registration rejected for user {UserId}: {Reason}", user.Id, result.Failure?.Message);
            throw new InvalidPasskeyException(VerificationFailedMessage);
        }

        if (!string.Equals(result.UserEntity.Id, user.Id.ToString(), StringComparison.Ordinal))
            throw new InvalidPasskeyException(VerificationFailedMessage);

        // A credential id is globally unique; one that already belongs to someone else is never re-bound.
        var owner = await userManager.FindByPasskeyIdAsync(result.Passkey.CredentialId);
        if (owner is not null && owner.Id != user.Id)
            throw new InvalidPasskeyException(VerificationFailedMessage);

        var passkey = result.Passkey;
        passkey.Name = NormalizeName(name);

        var saved = await userManager.AddOrUpdatePasskeyAsync(user, passkey);
        if (!saved.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to store the passkey: {string.Join(", ", saved.Errors.Select(e => e.Code))}");
        }

        return passkey;
    }

    public async Task<PasskeyOptionsResult> BeginAssertionAsync(CancellationToken ct = default)
    {
        EnsureEnabled();

        // No user: discoverable-credential (usernameless) sign-in, which also powers conditional UI (autofill).
        // It never reveals whether an account exists or which credentials it has.
        var request = await handler.MakeRequestOptionsAsync(null, Http);
        var ceremonyId = await ceremonies.CreateAsync(
            new PasskeyCeremony(PasskeyCeremonyKind.Assertion, null, request.AssertionState ?? string.Empty),
            CeremonyLifetime, ct);

        return new PasskeyOptionsResult(ceremonyId, request.RequestOptionsJson, ExpiresAt());
    }

    public async Task<(ApplicationUser User, UserPasskeyInfo Passkey)> CompleteAssertionAsync(
        string ceremonyId, string credentialJson, CancellationToken ct = default)
    {
        EnsureEnabled();

        var ceremony = await ceremonies.ConsumeAsync(ceremonyId, PasskeyCeremonyKind.Assertion, ct)
                       ?? throw new UnauthorizedException(InvalidCeremonyMessage);

        var result = await handler.PerformAssertionAsync(new PasskeyAssertionContext
        {
            HttpContext = Http,
            CredentialJson = credentialJson,
            AssertionState = ceremony.State
        });

        if (!result.Succeeded)
        {
            logger.LogWarning("Passkey sign-in rejected: {Reason}", result.Failure?.Message);
            throw new UnauthorizedException(VerificationFailedMessage);
        }

        // Persist the new signature counter and backup state (clone detection relies on the counter moving forward).
        var updated = await userManager.AddOrUpdatePasskeyAsync(result.User, result.Passkey);
        if (!updated.Succeeded)
        {
            logger.LogWarning("Passkey counter update failed for user {UserId}", result.User.Id);
        }

        return (result.User, result.Passkey);
    }

    public async Task EnsureFreshSessionAsync(Guid userId, CancellationToken ct = default)
    {
        var sessionId = sessions.GetCurrentSessionId() ?? throw new PasskeyReauthenticationRequiredException();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var active = await refreshTokens.ListActiveFamiliesAsync(userId, now, ct);
        var current = active.FirstOrDefault(s => s.FamilyId == sessionId)
                      ?? throw new PasskeyReauthenticationRequiredException();

        if (now - current.SignedInAt > TimeSpan.FromMinutes(Math.Max(1, _options.FreshAuthMinutes)))
            throw new PasskeyReauthenticationRequiredException();
    }

    public static string EncodeId(byte[] credentialId) => Base64Url.EncodeToString(credentialId);

    public static byte[]? DecodeId(string id)
    {
        // WebAuthn caps credential ids at 1023 bytes (1364 base64url characters).
        if (string.IsNullOrWhiteSpace(id) || id.Length > 1366)
            return null;

        try
        {
            return Base64Url.DecodeFromChars(id);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string? NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length > 100 ? trimmed[..100] : trimmed;
    }

    public static bool HasRealEmail(ApplicationUser user) =>
        !string.IsNullOrEmpty(user.Email) &&
        !user.Email.EndsWith(ApplicationUser.PhonePlaceholderEmailDomain, StringComparison.OrdinalIgnoreCase);

    private DateTime ExpiresAt() => timeProvider.GetUtcNow().UtcDateTime.Add(CeremonyLifetime);
}
