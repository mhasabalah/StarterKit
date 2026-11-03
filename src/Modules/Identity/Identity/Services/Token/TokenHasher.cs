using System.Security.Cryptography;

namespace Identity.Services.Token;

/// <summary>
/// The one place that hashes opaque tokens (refresh tokens, blacklisted access tokens, security stamps).
/// Raw values are never stored or put in tokens; only their SHA-256 hash is.
/// </summary>
public static class TokenHasher
{
    /// <summary>Base64 SHA-256 of <paramref name="value"/> (44 chars).</summary>
    public static string Hash(string value) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>
    /// Hash of a user's ASP.NET Identity security stamp. Goes into access tokens and refresh-token rows
    /// so a stamp rotation (password change, revocation) invalidates both.
    /// </summary>
    public static string HashSecurityStamp(string? securityStamp) => Hash(securityStamp ?? string.Empty);
}
