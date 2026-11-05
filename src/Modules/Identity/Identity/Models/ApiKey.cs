using System.Security.Cryptography;
using Identity.Services.Token;

namespace Identity.Models;

/// <summary>
/// A long-lived machine credential owned by a user (and bound to the tenant that was active when it was created).
/// Wire format <c>sk_&lt;prefix&gt;_&lt;secret&gt;</c>: the prefix is public (listing, lookup index), the secret is 256 random bits.
/// Only the SHA-256 hash of the whole key is stored; the raw key exists once, in the creation response.
/// <para>
/// Effective permissions of a request authenticated by a key are the owner's role/membership permissions
/// INTERSECTED with <see cref="Scopes"/>, so a key can never do more than its owner and loses rights with them.
/// </para>
/// </summary>
public class ApiKey
{
    public const string KeyMarker = "sk";
    public const int PrefixLength = 8;
    public const int SecretBytes = 32;
    public const int MaxNameLength = 100;
    private const string PrefixAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Tenant the key acts in (multi-tenancy); null = tenant-less / single-tenant mode.</summary>
    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Public identifier shown in listings and used to find the row. Unique.</summary>
    public string Prefix { get; private set; } = string.Empty;

    /// <summary>Base64 SHA-256 of the full raw key.</summary>
    public string KeyHash { get; private set; } = string.Empty;

    /// <summary>Permission names this key is limited to.</summary>
    public List<string> Scopes { get; private set; } = [];

    public DateTime CreatedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public DateTime? LastUsedAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public ApplicationUser User { get; private set; } = null!;

    public bool IsRevoked => RevokedAt.HasValue;

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAt;

    public bool IsActive(DateTime nowUtc) => !IsRevoked && !IsExpired(nowUtc);

    private ApiKey()
    {
    }

    /// <summary>Creates a key and returns it together with the raw key (the only time it is available).</summary>
    public static (ApiKey Key, string RawKey) Create(
        Guid userId, Guid? tenantId, string name, IEnumerable<string> scopes, DateTime nowUtc, DateTime expiresAt)
    {
        var prefix = RandomNumberGenerator.GetString(PrefixAlphabet, PrefixLength);
        var secret = Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));
        var raw = $"{KeyMarker}_{prefix}_{secret}";

        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Name = name.Trim(),
            Prefix = prefix,
            KeyHash = TokenHasher.Hash(raw),
            Scopes = scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            CreatedAt = nowUtc,
            ExpiresAt = expiresAt
        };

        return (key, raw);
    }

    /// <summary>Extracts the public prefix from a presented key; false when the shape is not ours.</summary>
    public static bool TryParsePrefix(string? rawKey, out string prefix)
    {
        prefix = string.Empty;
        if (string.IsNullOrEmpty(rawKey) || rawKey.Length > 256)
            return false;

        var parts = rawKey.Split('_', 3);
        if (parts.Length != 3 || parts[0] != KeyMarker || parts[1].Length != PrefixLength || parts[2].Length < 32)
            return false;

        if (!parts[1].All(char.IsAsciiLetterOrDigit))
            return false;

        prefix = parts[1];
        return true;
    }

    /// <summary>Constant-time comparison of the presented key against the stored hash.</summary>
    public bool Matches(string rawKey) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(TokenHasher.Hash(rawKey)), Encoding.UTF8.GetBytes(KeyHash));

    public bool Revoke(DateTime nowUtc, Guid? revokedBy)
    {
        if (IsRevoked)
            return false;
        RevokedAt = nowUtc;
        RevokedBy = revokedBy;
        return true;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
