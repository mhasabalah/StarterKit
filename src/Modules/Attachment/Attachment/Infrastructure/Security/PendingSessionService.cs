using System.Security.Cryptography;
using System.Text;

namespace Attachment.Infrastructure.Security;

/// <summary>
/// Server-issued pending upload sessions.
/// <para>
/// A session id is a normal <see cref="Guid"/> whose 16 bytes are <c>nonce(8) || HMAC(owner, nonce)(8)</c>.
/// The owner is the authenticated user (<c>u:{userId}</c>) or, for anonymous uploads, a fingerprint of the
/// client IP + User-Agent (<c>a:{sha256}</c>). Nothing is stored: ownership is proven by recomputing the
/// MAC, so a client-chosen GUID (or one issued to somebody else) is rejected, and every upload of a
/// session is counted against one owner-bound id.
/// </para>
/// </summary>
public interface IPendingSessionService
{
    /// <summary>Issues a new session bound to the current caller (user, or anonymous fingerprint).</summary>
    Guid IssueForCurrentCaller();

    /// <summary>True when the current caller owns the session (as the user, or by anonymous fingerprint).</summary>
    bool IsOwnedByCurrentCaller(Guid sessionId);

    /// <summary>
    /// True when the session was issued to <paramref name="userId"/>, or to the anonymous fingerprint of the
    /// current request (an anonymous uploader who logged in before associating).
    /// </summary>
    bool IsOwnedBy(Guid sessionId, Guid userId);
}

public sealed class PendingSessionService(
    IAttachmentSigningKeys keys,
    IHttpContextService httpContext) : IPendingSessionService
{
    private const string Purpose = "pending-session-v1";
    private const int NonceLength = 8;
    private const int MacLength = 8;

    public Guid IssueForCurrentCaller()
    {
        var userId = httpContext.GetUserIdAsGuid();
        var owner = userId.HasValue ? UserOwner(userId.Value) : AnonymousOwner();
        return Issue(owner);
    }

    public bool IsOwnedByCurrentCaller(Guid sessionId)
    {
        var userId = httpContext.GetUserIdAsGuid();
        if (userId.HasValue && Verify(sessionId, UserOwner(userId.Value)))
            return true;

        return Verify(sessionId, AnonymousOwner());
    }

    public bool IsOwnedBy(Guid sessionId, Guid userId) =>
        Verify(sessionId, UserOwner(userId)) || Verify(sessionId, AnonymousOwner());

    internal Guid Issue(string owner)
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes[..NonceLength]);
        ComputeMac(owner, bytes[..NonceLength], bytes[NonceLength..]);
        return new Guid(bytes);
    }

    internal bool Verify(Guid sessionId, string owner)
    {
        if (sessionId == Guid.Empty)
            return false;

        Span<byte> bytes = stackalloc byte[16];
        sessionId.TryWriteBytes(bytes);

        Span<byte> expected = stackalloc byte[MacLength];
        ComputeMac(owner, bytes[..NonceLength], expected);
        return CryptographicOperations.FixedTimeEquals(expected, bytes[NonceLength..]);
    }

    private void ComputeMac(string owner, ReadOnlySpan<byte> nonce, Span<byte> destination)
    {
        var ownerBytes = Encoding.UTF8.GetBytes(owner);
        var message = new byte[nonce.Length + 1 + ownerBytes.Length];
        nonce.CopyTo(message);
        message[nonce.Length] = (byte)'|';
        ownerBytes.CopyTo(message, nonce.Length + 1);

        Span<byte> mac = stackalloc byte[32];
        HMACSHA256.HashData(keys.GetKey(Purpose), message, mac);
        mac[..destination.Length].CopyTo(destination);
    }

    private static string UserOwner(Guid userId) => $"u:{userId:N}";

    private string AnonymousOwner()
    {
        var ip = httpContext.GetIpAddress() ?? "unknown-ip";
        var userAgent = httpContext.GetUserAgent() ?? "unknown-ua";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{ip}\n{userAgent}"));
        return "a:" + Convert.ToHexString(hash);
    }
}
