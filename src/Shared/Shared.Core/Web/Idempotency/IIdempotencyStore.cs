using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace Shared.Core.Web.Idempotency;

/// <summary>
/// A stored (or in-flight) idempotent request. <see cref="Body"/> is null while in flight. An in-flight entry carries
/// the random <see cref="Owner"/> token of the request that reserved it: only that request may complete or release it.
/// </summary>
public sealed record IdempotencyEntry(
    bool Completed,
    string Fingerprint,
    int StatusCode = 0,
    string? ContentType = null,
    string? Location = null,
    byte[]? Body = null,
    string? Owner = null);

public enum IdempotencyReservationStatus
{
    /// <summary>The caller owns the key and must call Complete or Release.</summary>
    Reserved,
    /// <summary>Another request with the same key is still running.</summary>
    InProgress,
    /// <summary>The key finished earlier; <see cref="IdempotencyReservation.Entry"/> holds the response.</summary>
    Completed,
    /// <summary>The key exists but was used with a different request body.</summary>
    FingerprintMismatch
}

/// <param name="Owner">For <see cref="IdempotencyReservationStatus.Reserved"/>: the token to pass to Complete/Release.</param>
public readonly record struct IdempotencyReservation(
    IdempotencyReservationStatus Status, IdempotencyEntry? Entry = null, string? Owner = null);

/// <summary>
/// Atomic key reservation + response storage behind <c>.WithIdempotency()</c>. Implementations throw when the store
/// is unreachable; <see cref="IdempotencyFilter"/> turns that into "process normally" (cache failures never fail a
/// request).
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyReservation> TryReserveAsync(string key, string fingerprint, TimeSpan inFlightTtl, CancellationToken ct);

    /// <summary>
    /// Stores the response, but only while the reservation is still this <paramref name="owner"/>'s (compare-and-set):
    /// after the in-flight TTL expired a newer request may own the key. Returns false when it no longer does.
    /// </summary>
    Task<bool> CompleteAsync(string key, string owner, IdempotencyEntry entry, TimeSpan ttl, CancellationToken ct);

    /// <summary>Deletes the key only if it is still this <paramref name="owner"/>'s unfinished reservation.</summary>
    Task<bool> ReleaseAsync(string key, string owner, CancellationToken ct);
}

internal static class IdempotencyEntryJson
{
    public static string Serialize(IdempotencyEntry entry) => JsonSerializer.Serialize(entry);

    public static IdempotencyEntry? Deserialize(string? json) =>
        string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<IdempotencyEntry>(json);

    public static IdempotencyReservation Classify(IdempotencyEntry existing, string fingerprint) =>
        existing.Fingerprint != fingerprint
            ? new(IdempotencyReservationStatus.FingerprintMismatch)
            : existing.Completed
                ? new(IdempotencyReservationStatus.Completed, existing)
                : new(IdempotencyReservationStatus.InProgress);
}

/// <summary>
/// Redis <c>SET key value PX ttl NX</c>: atomic across every API instance. Complete/Release are compare-and-set /
/// compare-and-delete on the owner token (Lua), so a stale request can never overwrite or delete a newer reservation.
/// </summary>
public sealed class RedisIdempotencyStore(IConnectionMultiplexer redis, string keyPrefix) : IIdempotencyStore
{
    // The reservation JSON is System.Text.Json output (no spaces): match the owner token and Completed=false textually
    // instead of decoding (a completed body can be large). Tokens are hex, so a plain find is exact.
    private const string CompleteScript = """
        local v = redis.call('GET', KEYS[1])
        if v and string.find(v, '"Completed":false', 1, true) and string.find(v, '"Owner":"' .. ARGV[1] .. '"', 1, true) then
            redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3])
            return 1
        end
        return 0
        """;

    private const string ReleaseScript = """
        local v = redis.call('GET', KEYS[1])
        if v and string.find(v, '"Completed":false', 1, true) and string.find(v, '"Owner":"' .. ARGV[1] .. '"', 1, true) then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    public async Task<IdempotencyReservation> TryReserveAsync(
        string key, string fingerprint, TimeSpan inFlightTtl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var db = redis.GetDatabase();
        var redisKey = keyPrefix + key;
        var owner = Guid.NewGuid().ToString("N");
        var placeholder = IdempotencyEntryJson.Serialize(new IdempotencyEntry(false, fingerprint, Owner: owner));

        // Two rounds: the existing entry can expire between the failed SET NX and the GET.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (await db.StringSetAsync(redisKey, placeholder, inFlightTtl, When.NotExists))
                return new(IdempotencyReservationStatus.Reserved, Owner: owner);

            var existing = IdempotencyEntryJson.Deserialize(await db.StringGetAsync(redisKey));
            if (existing is not null)
                return IdempotencyEntryJson.Classify(existing, fingerprint);
        }

        return new(IdempotencyReservationStatus.InProgress);
    }

    public async Task<bool> CompleteAsync(
        string key, string owner, IdempotencyEntry entry, TimeSpan ttl, CancellationToken ct)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(
            CompleteScript,
            [(RedisKey)(keyPrefix + key)],
            [owner, IdempotencyEntryJson.Serialize(entry), (long)ttl.TotalMilliseconds]);
        return (long)result == 1;
    }

    public async Task<bool> ReleaseAsync(string key, string owner, CancellationToken ct)
    {
        var result = await redis.GetDatabase().ScriptEvaluateAsync(
            ReleaseScript, [(RedisKey)(keyPrefix + key)], [owner]);
        return (long)result == 1;
    }
}

/// <summary>
/// Fallback over <see cref="IDistributedCache"/> when Redis is disabled (in-process memory cache). Check-then-set is
/// serialized by a process-wide lock, so it is exact for a single instance only; the owner check mirrors the Redis
/// store as best effort.
/// </summary>
public sealed class DistributedCacheIdempotencyStore(IDistributedCache cache) : IIdempotencyStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IdempotencyReservation> TryReserveAsync(
        string key, string fingerprint, TimeSpan inFlightTtl, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var existing = IdempotencyEntryJson.Deserialize(await cache.GetStringAsync(key, ct));
            if (existing is not null)
                return IdempotencyEntryJson.Classify(existing, fingerprint);

            var owner = Guid.NewGuid().ToString("N");
            await cache.SetStringAsync(key,
                IdempotencyEntryJson.Serialize(new IdempotencyEntry(false, fingerprint, Owner: owner)),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = inFlightTtl }, ct);
            return new(IdempotencyReservationStatus.Reserved, Owner: owner);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<bool> CompleteAsync(
        string key, string owner, IdempotencyEntry entry, TimeSpan ttl, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (!await StillOwnsAsync(key, owner, ct))
                return false;

            await cache.SetStringAsync(key, IdempotencyEntryJson.Serialize(entry),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<bool> ReleaseAsync(string key, string owner, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (!await StillOwnsAsync(key, owner, ct))
                return false;

            await cache.RemoveAsync(key, ct);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<bool> StillOwnsAsync(string key, string owner, CancellationToken ct)
    {
        var current = IdempotencyEntryJson.Deserialize(await cache.GetStringAsync(key, ct));
        return current is { Completed: false } && current.Owner == owner;
    }
}
