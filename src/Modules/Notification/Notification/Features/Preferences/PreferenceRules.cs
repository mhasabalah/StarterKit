using System.Globalization;
using Notification.Contracts.Preferences;

namespace Notification.Features.Preferences;

/// <summary>Daily quiet window as sent and returned by the API (<c>HH:mm</c> in the preference's time zone).</summary>
/// <param name="Enabled">False on update removes the window (and its time zone); on read it is always true.</param>
public sealed record QuietHoursDto(bool Enabled, string? Start = null, string? End = null);

/// <summary>Validation and mapping shared by the "my" and admin preference endpoints.</summary>
public static class PreferenceRules
{
    private static readonly string[] TimeFormats = ["HH:mm", "HH:mm:ss"];

    /// <summary>
    /// Validates <paramref name="request"/> and turns it into the change to store. Returns null and sets
    /// <paramref name="error"/> (localized) when it is invalid.
    /// </summary>
    public static PreferenceChange? Build(
        UpdatePreferenceRequest request,
        DeliveryPreference? existing,
        IEnumerable<INotificationChannelAdapter> adapters,
        IStringLocalizer<NotificationResource> localizer,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        error = ChannelOrderRules.Validate(request.ChannelOrder, adapters);
        if (error is not null)
            return null;

        Dictionary<string, ChannelMuteSet>? mutes = null;
        if (request.CategoryMutes is not null)
        {
            mutes = new Dictionary<string, ChannelMuteSet>(StringComparer.Ordinal);
            foreach (var (rawGroup, set) in request.CategoryMutes)
            {
                var group = rawGroup?.Trim().ToLowerInvariant() ?? string.Empty;
                if (!NotificationCategoryGroups.IsKnown(group))
                {
                    error = localizer["Validation.Preference.UnknownGroup", rawGroup ?? string.Empty];
                    return null;
                }

                if (set is null || set.IsEmpty)
                    continue;

                foreach (var channel in new[] { NotificationChannel.InApp, NotificationChannel.Push, NotificationChannel.Email })
                {
                    if (set.IsMuted(channel) && !NotificationCategoryGroups.IsChannelMutable(group, channel))
                    {
                        error = localizer["Validation.Preference.ChannelNotMutable", group, channel.ToString()];
                        return null;
                    }
                }

                mutes[group] = mutes.TryGetValue(group, out var prior)
                    ? new ChannelMuteSet(prior.InApp || set.InApp, prior.Push || set.Push, prior.Email || set.Email)
                    : set;
            }
        }

        var timeZone = request.TimeZone?.Trim();
        if (timeZone is { Length: > 0 })
        {
            timeZone = CanonicalIanaId(timeZone);
            if (timeZone is null)
            {
                error = localizer["Validation.Preference.TimeZone.Invalid"];
                return null;
            }
        }

        TimeOnly? start = null, end = null;
        var clear = false;
        if (request.QuietHours is { } quiet)
        {
            if (!quiet.Enabled)
            {
                clear = true;
            }
            else if (!TryParseTime(quiet.Start, out var s) || !TryParseTime(quiet.End, out var e))
            {
                error = localizer["Validation.Preference.QuietHours.Invalid"];
                return null;
            }
            else if (s == e)
            {
                error = localizer["Validation.Preference.QuietHours.SameTime"];
                return null;
            }
            else if (string.IsNullOrEmpty(timeZone) && string.IsNullOrEmpty(existing?.TimeZone))
            {
                error = localizer["Validation.Preference.TimeZone.Required"];
                return null;
            }
            else
            {
                (start, end) = (s, e);
            }
        }

        return new PreferenceChange(request.ChannelOrder, request.PushEnabled, mutes, start, end,
            string.IsNullOrEmpty(timeZone) ? null : timeZone, clear);
    }

    /// <summary>Validates and stores an update for <paramref name="userId"/>, then returns the stored preference.</summary>
    public static async Task<IResult> ApplyAsync(
        Guid userId,
        UpdatePreferenceRequest request,
        INotificationPreferenceRepository preferences,
        IEnumerable<INotificationChannelAdapter> adapters,
        IStringLocalizer<NotificationResource> localizer,
        CancellationToken ct)
    {
        var existing = await preferences.GetDeliveryPreferenceAsync(userId, ct);
        var change = Build(request, existing, adapters, localizer, out var error);
        if (change is null)
            return ApiResponseExtensions.BadRequest(error!);

        var now = await preferences.UpdateAsync(userId, change, ct);
        var stored = await preferences.GetDeliveryPreferenceAsync(userId, ct);

        return Results.Ok(ApiResponseExtensions.Success(new UpdatePreferenceResponse(
            userId, request.ChannelOrder, now, stored?.PushEnabled ?? true,
            stored?.CategoryMutes ?? new Dictionary<string, ChannelMuteSet>(),
            ToQuietHours(stored?.QuietHoursStart, stored?.QuietHoursEnd), stored?.TimeZone)));
    }

    /// <summary>
    /// The IANA id to store for <paramref name="input"/>: an IANA id is kept as is, a Windows id (<c>Egypt Standard Time</c>)
    /// is converted. Null when the id is unknown or has no IANA equivalent, so stored values are portable between hosts.
    /// </summary>
    public static string? CanonicalIanaId(string input)
    {
        if (input.Length > 64 || !TimeZoneInfo.TryFindSystemTimeZoneById(input, out var zone))
            return null;

        if (zone.HasIanaId)
            return zone.Id;

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : null;
    }

    public static QuietHoursDto? ToQuietHours(TimeOnly? start, TimeOnly? end) =>
        start is { } s && end is { } e
            ? new QuietHoursDto(true, s.ToString("HH:mm", CultureInfo.InvariantCulture), e.ToString("HH:mm", CultureInfo.InvariantCulture))
            : null;

    public static MyPreferenceDto ToDto(Guid userId, UserNotificationPreference? stored, NotificationChannel[] defaultOrder) =>
        stored is null
            ? new MyPreferenceDto(userId, defaultOrder, IsDefault: true)
            : new MyPreferenceDto(userId, stored.ChannelOrder, IsDefault: false, stored.PushEnabled,
                stored.CategoryMutes, ToQuietHours(stored.QuietHoursStart, stored.QuietHoursEnd), stored.TimeZone);

    private static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
