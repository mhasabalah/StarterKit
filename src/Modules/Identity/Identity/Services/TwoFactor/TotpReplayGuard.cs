using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace Identity.Services.TwoFactor;

/// <summary>
/// Makes every accepted authenticator (TOTP) code single-use. ASP.NET Identity's
/// <see cref="AuthenticatorTokenProvider{TUser}"/> accepts a code for the current 30 s step and two steps either side,
/// so without this a code seen once (shoulder-surfed, phished in real time, logged by a proxy) can be replayed for up
/// to <see cref="Window"/>.
/// <para>
/// The provider does not report which time step matched, so the guard records (user, code) - the code is stored
/// as a SHA-256 hash bound to the user id, never in clear. A different valid code (the next step) is still accepted.
/// The guard is consulted only AFTER the code validated, so a wrong guess never burns a code.
/// </para>
/// <para>
/// Failure mode: <b>fail closed</b>. If the store is unreachable the exception propagates (the request fails with
/// 500, no lockout strike is recorded) rather than silently dropping replay protection on an admin login.
/// </para>
/// </summary>
public interface ITotpReplayGuard
{
    /// <summary>
    /// Atomically records the code as used. True on first use; false when the same user already used the same code
    /// inside <see cref="TotpReplayGuard.Window"/>.
    /// </summary>
    Task<bool> TryConsumeAsync(Guid userId, string code, CancellationToken cancellationToken = default);
}

public static class TotpReplayGuard
{
    /// <summary>
    /// How long a used code is remembered: the provider accepts steps -2..+2 (5 x 30 s = 150 s), plus a margin for
    /// clock drift between instances.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(180);

    public const string KeySegment = "identity:2fa:totp-used:";

    /// <summary>Key suffix: user id + SHA-256 of "userId:code" (so the store never holds a usable code).</summary>
    public static string KeyFor(Guid userId, string code) =>
        $"{KeySegment}{userId:N}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{code}")))}";
}

/// <summary>Redis <c>SET key 1 PX ttl NX</c>: atomic across every API instance.</summary>
public sealed class RedisTotpReplayGuard(IConnectionMultiplexer redis, string keyPrefix) : ITotpReplayGuard
{
    public async Task<bool> TryConsumeAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await redis.GetDatabase().StringSetAsync(
            keyPrefix + TotpReplayGuard.KeyFor(userId, code), "1", TotpReplayGuard.Window, When.NotExists);
    }
}

/// <summary>
/// Fallback over <see cref="IDistributedCache"/> when Redis is disabled (<c>Redis:Enabled=false</c>: in-process memory
/// cache). Check-then-set is serialized by a process-wide lock, so it is exact for a single instance only - the same
/// limit as the rest of the Redis-less mode.
/// </summary>
public sealed class DistributedCacheTotpReplayGuard(IDistributedCache cache) : ITotpReplayGuard
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<bool> TryConsumeAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var key = TotpReplayGuard.KeyFor(userId, code);
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (await cache.GetAsync(key, cancellationToken) is not null)
            {
                return false;
            }

            await cache.SetAsync(key, [1],
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TotpReplayGuard.Window },
                cancellationToken);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }
}
