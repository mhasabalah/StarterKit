using System.Security.Cryptography;
using Identity.Options;

namespace Identity.Services.Token;

/// <summary>
/// HS256 signing keys with rotation support. Tokens are signed with the CURRENT <c>JwtSettings:SecretKey</c> and carry
/// a <c>kid</c> header; validation accepts the current key plus <c>JwtSettings:PreviousSecretKeys</c>, so tokens issued
/// before a rotation stay valid until they expire. Remove a previous key once its tokens are gone (access-token
/// lifetime), or at once to revoke everything it signed.
/// <para>The <c>kid</c> is a public fingerprint (first 8 bytes of SHA-256 of the key, hex) - it never reveals the key.</para>
/// </summary>
public static class JwtSigningKeys
{
    /// <summary>First 8 bytes of SHA-256(key), lower-case hex.</summary>
    public static string KeyId(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)).AsSpan(0, 8));

    public static SymmetricSecurityKey Create(string secret) =>
        new(Encoding.UTF8.GetBytes(secret)) { KeyId = KeyId(secret) };

    /// <summary>The key new tokens are signed with.</summary>
    public static SymmetricSecurityKey Current(JwtOptions options) => Create(options.SecretKey);

    /// <summary>Every key a token may validate against: current first, then the previous ones.</summary>
    public static IReadOnlyList<SecurityKey> ValidationKeys(JwtOptions options) =>
    [
        Current(options),
        .. options.PreviousSecretKeys
            .Where(k => !string.IsNullOrWhiteSpace(k) && !string.Equals(k, options.SecretKey, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Select(Create)
    ];
}
