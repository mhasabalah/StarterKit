using Notification.Contracts.Preferences;

namespace Notification.Dispatch;

/// <summary>
/// The user's mutes and quiet hours applied to one delivery. Pure functions of the stored preference, the category and the
/// clock, so the matrix is unit-tested without a database.
/// <list type="bullet">
/// <item>Mutes are per category GROUP (the prefix before the first dot) and per channel (in-app, push, email).
/// <see cref="NotificationCategoryGroups"/> fixes some channels (all of <c>security</c>, <c>billing</c> email): a stale
/// stored mute on them is ignored.</item>
/// <item>"Email" covers the whole email/WhatsApp fallback chain.</item>
/// <item>A delivery with no category is never muted and never deferred.</item>
/// <item><c>security</c> is never held back by quiet hours.</item>
/// </list>
/// </summary>
public static class NotificationPolicy
{
    /// <summary>True when the user muted <paramref name="channel"/> for the category's group (and the channel is mutable).</summary>
    public static bool IsMuted(DeliveryPreference? preference, string? category, NotificationChannel channel)
    {
        if (preference is null || string.IsNullOrWhiteSpace(category))
            return false;

        var group = NotificationCategoryGroups.GroupOf(category);
        if (!NotificationCategoryGroups.IsChannelMutable(group, channel))
            return false;

        return preference.CategoryMutes.TryGetValue(group, out var muted) && muted.IsMuted(channel);
    }

    /// <summary>
    /// When <paramref name="now"/> falls inside the user's quiet window, the instant it ends; otherwise <c>null</c>.
    /// The window is [start, end) on the user's local clock and may cross midnight. An unknown time zone means no quiet
    /// hours (never block delivery on bad data). An end time that does not exist (spring-forward gap) ends at the first
    /// valid local time after it; an ambiguous one (fall-back) ends at its first occurrence.
    /// </summary>
    public static DateTimeOffset? QuietHoursEnd(DeliveryPreference? preference, string? category, DateTimeOffset now)
    {
        if (preference is not { QuietHoursStart: { } start, QuietHoursEnd: { } end, TimeZone: { Length: > 0 } zoneId } || start == end)
            return null;

        if (string.IsNullOrWhiteSpace(category) || !NotificationCategoryGroups.DefersForQuietHours(NotificationCategoryGroups.GroupOf(category)))
            return null;

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone))
            return null;

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var time = TimeOnly.FromTimeSpan(local.TimeOfDay);
        var inside = start < end ? time >= start && time < end : time >= start || time < end;
        if (!inside)
            return null;

        // The next occurrence of `end` on the local clock (tomorrow when it is already past, e.g. 23:00 in 22:00-07:00).
        var endLocal = DateTime.SpecifyKind(local.Date + end.ToTimeSpan(), DateTimeKind.Unspecified);
        if (endLocal <= local.DateTime)
            endLocal = endLocal.AddDays(1);

        while (zone.IsInvalidTime(endLocal))
            endLocal = endLocal.AddMinutes(1); // spring-forward gap: the first local time that exists

        var end0 = zone.IsAmbiguousTime(endLocal)
            ? new DateTimeOffset(endLocal, zone.GetAmbiguousTimeOffsets(endLocal).Max())
            : new DateTimeOffset(endLocal, zone.GetUtcOffset(endLocal));
        return end0.ToUniversalTime();
    }
}
