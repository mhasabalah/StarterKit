namespace Notification.Persistence;

/// <summary>What delivery jobs need to know about a user: push switch, per-group mutes and the quiet window.</summary>
public sealed record DeliveryPreference(
    bool PushEnabled,
    IReadOnlyDictionary<string, ChannelMuteSet> CategoryMutes,
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,
    string? TimeZone);

/// <summary>
/// A change to a user's preference. Null members keep the stored value. <see cref="CategoryMutes"/> REPLACES the whole
/// map; <see cref="ClearQuietHours"/> removes the window (and its time zone).
/// </summary>
public sealed record PreferenceChange(
    NotificationChannel[] ChannelOrder,
    bool? PushEnabled = null,
    IReadOnlyDictionary<string, ChannelMuteSet>? CategoryMutes = null,
    TimeOnly? QuietHoursStart = null,
    TimeOnly? QuietHoursEnd = null,
    string? TimeZone = null,
    bool ClearQuietHours = false);

public interface INotificationPreferenceRepository
{
    /// <summary>The delivery-relevant preference, or <c>null</c> when the user has none (defaults apply: nothing muted).</summary>
    Task<DeliveryPreference?> GetDeliveryPreferenceAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Applies <paramref name="change"/> (insert or update) and returns the new <c>UpdatedAt</c>.</summary>
    Task<DateTime> UpdateAsync(Guid userId, PreferenceChange change, CancellationToken ct = default);

    /// <summary>The user's stored channel order, or <c>null</c> when they have none.</summary>
    Task<NotificationChannel[]?> GetChannelOrderAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's channel order, falling back to <see cref="NotificationOptions.DefaultChannelOrder"/>.</summary>
    Task<NotificationChannel[]> ResolveChannelOrderAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Stores the channel order; <paramref name="pushEnabled"/> null keeps the current push setting (default on).</summary>
    Task<DateTime> UpsertAsync(Guid userId, NotificationChannel[] channelOrder, bool? pushEnabled = null, CancellationToken ct = default);

    /// <summary>False only when the user explicitly turned push off.</summary>
    Task<bool> IsPushEnabledAsync(Guid userId, CancellationToken ct = default);
}

public sealed class NotificationPreferenceRepository(
    NotificationDbContext db,
    IOptions<NotificationOptions> options) : INotificationPreferenceRepository
{
    public async Task<NotificationChannel[]?> GetChannelOrderAsync(Guid userId, CancellationToken ct = default)
    {
        var preference = await db.UserNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, ct);

        return preference is { ChannelOrder.Length: > 0 } ? preference.ChannelOrder : null;
    }

    public async Task<NotificationChannel[]> ResolveChannelOrderAsync(Guid userId, CancellationToken ct = default)
        => await GetChannelOrderAsync(userId, ct) ?? options.Value.DefaultChannelOrder;

    public async Task<bool> IsPushEnabledAsync(Guid userId, CancellationToken ct = default) =>
        await db.UserNotificationPreferences.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => (bool?)x.PushEnabled)
            .FirstOrDefaultAsync(ct) ?? true;

    public async Task<DeliveryPreference?> GetDeliveryPreferenceAsync(Guid userId, CancellationToken ct = default) =>
        await db.UserNotificationPreferences.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new DeliveryPreference(x.PushEnabled, x.CategoryMutes, x.QuietHoursStart, x.QuietHoursEnd, x.TimeZone))
            .FirstOrDefaultAsync(ct);

    public Task<DateTime> UpsertAsync(
        Guid userId, NotificationChannel[] channelOrder, bool? pushEnabled = null, CancellationToken ct = default) =>
        UpdateAsync(userId, new PreferenceChange(channelOrder, pushEnabled), ct);

    public async Task<DateTime> UpdateAsync(Guid userId, PreferenceChange change, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        var now = DateTime.UtcNow;
        var existing = await db.UserNotificationPreferences.FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (existing is null)
        {
            existing = new UserNotificationPreference { Id = Guid.NewGuid(), UserId = userId };
            db.UserNotificationPreferences.Add(existing);
        }

        existing.ChannelOrder = change.ChannelOrder;
        existing.PushEnabled = change.PushEnabled ?? existing.PushEnabled;
        existing.UpdatedAt = now;

        if (change.CategoryMutes is not null)
        {
            existing.CategoryMutes = change.CategoryMutes
                .Where(kv => !kv.Value.IsEmpty)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }

        if (change.ClearQuietHours)
        {
            existing.QuietHoursStart = existing.QuietHoursEnd = null;
            existing.TimeZone = null;
        }
        else if (change.QuietHoursStart is { } start && change.QuietHoursEnd is { } end)
        {
            existing.QuietHoursStart = start;
            existing.QuietHoursEnd = end;
        }

        // A time zone may be changed on its own (travel) while a window exists.
        if (!change.ClearQuietHours && change.TimeZone is not null)
            existing.TimeZone = change.TimeZone;

        await db.SaveChangesAsync(ct);
        return now;
    }
}
