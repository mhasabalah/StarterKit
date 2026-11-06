namespace Notification.Persistence.Entities;

/// <summary>Channels a user muted for one category group (true = muted).</summary>
public sealed record ChannelMuteSet(bool InApp = false, bool Push = false, bool Email = false)
{
    public bool IsEmpty => !InApp && !Push && !Email;

    public bool IsMuted(NotificationChannel channel) => channel switch
    {
        NotificationChannel.InApp => InApp,
        NotificationChannel.Push => Push,
        NotificationChannel.Email => Email,
        _ => false
    };
}

public sealed class UserNotificationPreference
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public NotificationChannel[] ChannelOrder { get; set; } = [];

    /// <summary>False = the user turned push notifications off (the in-app inbox is always kept).</summary>
    public bool PushEnabled { get; set; } = true;

    /// <summary>Muted channels per category group (see <c>NotificationCategoryGroups</c>); absent group = nothing muted.</summary>
    public Dictionary<string, ChannelMuteSet> CategoryMutes { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Start of the daily quiet window in <see cref="TimeZone"/>; the window may cross midnight. Null = none.</summary>
    public TimeOnly? QuietHoursStart { get; set; }

    public TimeOnly? QuietHoursEnd { get; set; }

    /// <summary>IANA time zone id the quiet window is read in (validated with <c>TimeZoneInfo</c>).</summary>
    public string? TimeZone { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
