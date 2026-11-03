namespace Notification.Persistence;

public interface INotificationPreferenceRepository
{
    /// <summary>The user's stored channel order, or <c>null</c> when they have none.</summary>
    Task<NotificationChannel[]?> GetChannelOrderAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's channel order, falling back to <see cref="NotificationOptions.DefaultChannelOrder"/>.</summary>
    Task<NotificationChannel[]> ResolveChannelOrderAsync(Guid userId, CancellationToken ct = default);

    Task<DateTime> UpsertAsync(Guid userId, NotificationChannel[] channelOrder, CancellationToken ct = default);
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

    public async Task<DateTime> UpsertAsync(Guid userId, NotificationChannel[] channelOrder, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var existing = await db.UserNotificationPreferences.FirstOrDefaultAsync(x => x.UserId == userId, ct);

        if (existing is null)
        {
            db.UserNotificationPreferences.Add(new UserNotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ChannelOrder = channelOrder,
                UpdatedAt = now
            });
        }
        else
        {
            existing.ChannelOrder = channelOrder;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return now;
    }
}
