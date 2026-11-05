namespace Notification.Contracts.Email.Models;

/// <summary>
/// "Something security-relevant changed on your account" (passkey added/removed, SSO identity linked, ...).
/// One template for every account-security event, so each new sign-in method does not need its own email.
/// Always sent with <c>NotificationChannel.Email</c> forced: channel preferences never suppress it.
/// </summary>
public sealed class SecurityAlertModel
{
    /// <summary>One of <see cref="SecurityAlertEvents"/> - selects the headline and body text.</summary>
    public string EventKey { get; set; } = SecurityAlertEvents.PasskeyAdded;

    /// <summary>What changed, e.g. the passkey name. Shown as data, HTML-encoded.</summary>
    public string? Detail { get; set; }

    /// <summary>When it happened, UTC, pre-formatted (<c>yyyy-MM-dd HH:mm 'UTC'</c>).</summary>
    public string OccurredAt { get; set; } = string.Empty;

    public string? IpAddress { get; set; }

    /// <summary>Where the user reviews their security settings (frontend URL).</summary>
    public string? ManageUrl { get; set; }

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["EventKey"] = EventKey,
        ["Detail"] = Detail ?? string.Empty,
        ["OccurredAt"] = OccurredAt,
        ["IpAddress"] = IpAddress ?? string.Empty,
        ["ManageUrl"] = ManageUrl ?? string.Empty
    };
}

/// <summary>Event keys understood by the <c>SecurityAlert</c> template.</summary>
public static class SecurityAlertEvents
{
    public const string PasskeyAdded = "passkey_added";
    public const string PasskeyRemoved = "passkey_removed";
    public const string SsoIdentityLinked = "sso_identity_linked";
}
