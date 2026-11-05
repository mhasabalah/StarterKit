namespace Identity.Tests;

/// <summary>
/// RFC 6238 TOTP (HMAC-SHA1, 30 s, 6 digits) - the authenticator-app side, used by tests to produce codes that
/// ASP.NET Identity's AuthenticatorTokenProvider must accept.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
    Justification = "RFC 6238 mandates HMAC-SHA1 for authenticator-app TOTP; this test helper mirrors the app side.")]
public static class Totp
{
    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        var key = FromBase32(base32Key.Replace(" ", string.Empty).ToUpperInvariant());
        var step = (long)((at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30);
        var counter = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);

        var hash = System.Security.Cryptography.HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// The code of the 30 s step <paramref name="stepsAhead"/> steps after now (0..2 are accepted by the server).
    /// The server rejects a code it already accepted (replay guard), so a test that uses the authenticator more than
    /// once takes strictly increasing <paramref name="stepsAhead"/> values: even across a step boundary they map to
    /// different steps.
    /// </summary>
    public static string Code(string base32Key, int stepsAhead) =>
        Code(base32Key, DateTimeOffset.UtcNow.AddSeconds(30 * stepsAhead));

    /// <summary>A valid-format code that is NOT valid for this key around now.</summary>
    public static string WrongCode(string base32Key)
    {
        var now = DateTimeOffset.UtcNow;
        var valid = Enumerable.Range(-3, 7).Select(i => Code(base32Key, now.AddSeconds(30 * i))).ToHashSet();
        for (var candidate = 0; ; candidate++)
        {
            var code = candidate.ToString("D6");
            if (!valid.Contains(code))
                return code;
        }
    }

    private static byte[] FromBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}
