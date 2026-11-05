using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Identity.Services.Passkeys;

/// <summary>Which ceremony a stored state belongs to (a sign-in state can never complete a registration).</summary>
public enum PasskeyCeremonyKind
{
    Registration,
    Assertion
}

/// <summary>Server-side state of one WebAuthn ceremony (the challenge lives in <see cref="State"/>).</summary>
public sealed record PasskeyCeremony(PasskeyCeremonyKind Kind, Guid? UserId, string State);

/// <summary>
/// Keeps WebAuthn ceremony state on the server instead of in a cookie: our clients are token-based SPAs and mobile
/// apps on other origins, so the built-in cookie-based <c>SignInManager.MakePasskey*</c> flow does not fit.
/// Each ceremony id is random (128 bits), bound to its kind and (for registration) to the user, expires after
/// <c>Identity:Passkeys:CeremonyTimeoutMinutes</c>, and is consumed on first use - a replayed or raced verify fails.
/// </summary>
public interface IPasskeyCeremonyStore
{
    Task<string> CreateAsync(PasskeyCeremony ceremony, TimeSpan lifetime, CancellationToken ct = default);

    /// <summary>Returns and deletes the ceremony; null when unknown, expired, already used or of another kind.</summary>
    Task<PasskeyCeremony?> ConsumeAsync(string ceremonyId, PasskeyCeremonyKind kind, CancellationToken ct = default);
}

public sealed class DistributedCachePasskeyCeremonyStore(IDistributedCache cache, Sso.IOneTimeUseGuard guard)
    : IPasskeyCeremonyStore
{
    private const string KeyPrefix = "identity:passkey-ceremony:";

    public async Task<string> CreateAsync(PasskeyCeremony ceremony, TimeSpan lifetime, CancellationToken ct = default)
    {
        var id = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        await cache.SetAsync(
            KeyPrefix + id,
            JsonSerializer.SerializeToUtf8Bytes(ceremony),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
            ct);
        return id;
    }

    public async Task<PasskeyCeremony?> ConsumeAsync(string ceremonyId, PasskeyCeremonyKind kind, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ceremonyId) || ceremonyId.Length > 64)
            return null;

        var key = KeyPrefix + ceremonyId;
        var bytes = await cache.GetAsync(key, ct);
        if (bytes is null)
            return null;

        // Exactly one caller wins the ceremony, across instances (Redis SET NX / local gate); then the state is removed.
        if (!await guard.TryUseAsync("passkey:" + ceremonyId, TimeSpan.FromMinutes(15), ct))
            return null;

        await cache.RemoveAsync(key, ct);

        var ceremony = JsonSerializer.Deserialize<PasskeyCeremony>(bytes);
        return ceremony?.Kind == kind ? ceremony : null;
    }
}
