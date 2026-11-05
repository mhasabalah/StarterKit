using System.Security.Cryptography;
using System.Text;

namespace Webhooks.Security;

/// <summary>
/// Standard Webhooks signing (https://www.standardwebhooks.com): signature = base64(HMAC-SHA256(key, "{id}.{timestamp}.{body}"))
/// sent as <c>v1,&lt;signature&gt;</c> (space-separated list when several are valid). The key is the base64 part of the
/// <c>whsec_...</c> secret.
/// </summary>
public static class WebhookSigner
{
    public const string SecretPrefix = "whsec_";
    public const string SignatureVersion = "v1";

    /// <summary>Default replay tolerance receivers should enforce on <c>webhook-timestamp</c>.</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    /// <summary>The <c>webhook-id</c> header value. Stable per event across retries and redeliveries.</summary>
    public static string MessageIdHeader(Guid messageId) => $"msg_{messageId:N}";

    public static string GenerateSecret() =>
        SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string Sign(string secret, Guid messageId, long timestamp, string body)
    {
        var key = KeyBytes(secret);
        var signed = Encoding.UTF8.GetBytes($"{MessageIdHeader(messageId)}.{timestamp}.{body}");
        return $"{SignatureVersion},{Convert.ToBase64String(HMACSHA256.HashData(key, signed))}";
    }

    /// <summary>Receiver-side check (also what the docs tell customers to do). Constant-time; honours the tolerance.</summary>
    public static bool Verify(string secret, string messageIdHeader, string timestampHeader, string body,
        string signatureHeader, DateTimeOffset now, TimeSpan? tolerance = null)
    {
        if (!long.TryParse(timestampHeader, out var timestamp)) return false;
        var skew = now - DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if (skew.Duration() > (tolerance ?? DefaultTolerance)) return false;

        var key = KeyBytes(secret);
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{messageIdHeader}.{timestamp}.{body}"));

        foreach (var candidate in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = candidate.Split(',', 2);
            if (parts.Length != 2 || parts[0] != SignatureVersion) continue;
            try
            {
                if (CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(parts[1]))) return true;
            }
            catch (FormatException)
            {
                // malformed candidate: ignore
            }
        }

        return false;
    }

    private static byte[] KeyBytes(string secret)
    {
        var encoded = secret.StartsWith(SecretPrefix, StringComparison.Ordinal) ? secret[SecretPrefix.Length..] : secret;
        return Convert.FromBase64String(encoded);
    }
}
