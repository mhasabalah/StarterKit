namespace Notification.Persistence.Entities;

/// <summary>
/// A device that receives push notifications for a user (<c>notification.push_device</c>). The FCM registration token
/// is globally unique: one device belongs to one user at a time (re-registering moves it). <see cref="SessionId"/> binds
/// the device to the sign-in that registered it - when that session ends, the device is dropped at the next send.
/// </summary>
public sealed class PushDevice
{
    public const int TokenHintLength = 8;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public PushPlatform Platform { get; set; }

    /// <summary>The <c>sid</c> (refresh-token family) of the registering session; null for tokens without one.</summary>
    public Guid? SessionId { get; set; }

    public string? AppVersion { get; set; }
    public string? Locale { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Last registration refresh (the app re-registers on every start). Stale devices are purged.</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>Last characters of the token, for display. The full token is never returned by the API.</summary>
    public static string Hint(string token) =>
        token.Length <= TokenHintLength ? new string('*', token.Length) : "..." + token[^TokenHintLength..];
}
