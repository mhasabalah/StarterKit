using Notification.Contracts.Preferences;

namespace Notification.Dispatch;

/// <summary>
/// The user's mutes and quiet hours applied to one delivery. Pure functions of the stored preference, the category and the
/// clock, so the matrix is unit-tested without a database.
/// <list type="bullet">
/// <item>Mutes are per category GROUP (the prefix before the first dot) and per channel (in-app, push, email).</item>
/// <item>The <c>security</c> group is never muted and never held back by quiet hours; email forced through
/// <c>SendAsync(NotificationChannel.Email)</c> (security mail) is never muted.</item>
/// <item>A message without a category belongs to <c>system</c>; an email-only send without a category is never muted.</item>
/// </list>
/// </summary>
public static class NotificationPolicy
{
    /// <summary>True when the user muted <paramref name="channel"/> for the category's group.</summary>
    public static bool IsMuted(DeliveryPreference? preference, string? category, NotificationChannel channel, bool forced = false)
    {
        if (preference is null || forced)
            return false;

        var group = NotificationCategoryGroups.GroupOf(category);
        if (!NotificationCategoryGroups.IsMutable(group))
            return false;

        return preference.CategoryMutes.TryGetValue(group, out var muted) && muted.IsMuted(channel);
    }

    /// <summary>
    /// When <paramref name="now"/> falls inside the user's quiet window, the instant it ends; otherwise <c>null</c>.
    /// The window is [start, end) on the user's local clock and may cross midnight. An unknown time zone means no quiet
    /// hours (never block delivery on bad data).
    /// </summary>
    public static DateTimeOffset? QuietHoursEnd(DeliveryPreference? preference, string? category, DateTimeOffset now)
    {
        if (preference is not { QuietHoursStart: { } start, QuietHoursEnd: { } end, TimeZone: { Length: > 0 } zoneId } || start == end)
            return null;

        if (!NotificationCategoryGroups.IsMutable(NotificationCategoryGroups.GroupOf(category)))
            return null;

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone))
            return null;

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var time = TimeOnly.FromTimeSpan(local.TimeOfDay);
        var inside = start < end ? time >= start && time < end : time >= start || time < end;
        if (!inside)
            return null;

        // The next occurrence of `end` on the local clock (tomorrow when it is already past, e.g. 23:00 in 22:00-07:00).
        var endLocal = local.Date + end.ToTimeSpan();
        if (endLocal <= local.DateTime)
            endLocal = endLocal.AddDays(1);
        if (zone.IsInvalidTime(endLocal))
            endLocal = endLocal.AddHours(1); // spring-forward gap

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
    }
}
