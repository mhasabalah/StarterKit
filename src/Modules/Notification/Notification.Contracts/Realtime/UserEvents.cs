namespace Notification.Contracts.Realtime;

/// <summary>
/// A transient, typed event for a user's open sessions (progress of their background work, a refresh hint), pushed
/// over the notification hub as <see cref="NotificationHubContract.UserEvent"/>. Unlike an inbox entry it is NOT stored:
/// a client that was offline misses it and re-reads the state over REST, which stays the source of truth.
/// </summary>
/// <param name="Type">Machine-readable kind, <c>&lt;module&gt;.&lt;event&gt;</c> (e.g. <c>datatransfer.job-progress</c>).</param>
/// <param name="TenantId">Tenant the event belongs to (null = no tenant / single-tenant); clients ignore other tenants.</param>
/// <param name="Data">Small payload of ids, counts and statuses; never PII or secrets.</param>
public sealed record UserEvent(string Type, Guid? TenantId, IReadOnlyDictionary<string, object?> Data)
{
    public const int TypeMaxLength = 100;
    public const int MaxDataEntries = 20;
}

/// <summary>
/// Sends <see cref="UserEvent"/>s to every open session of a user (all replicas, through the hub's backplane).
/// Best effort: never throws, and does nothing when real time is off (<c>Realtime:Enabled=false</c>).
/// </summary>
public interface IUserEventPublisher
{
    Task PublishAsync(Guid userId, UserEvent userEvent, CancellationToken cancellationToken = default);
}

/// <summary>Default when the Notification module (or real time) is not installed.</summary>
public sealed class NullUserEventPublisher : IUserEventPublisher
{
    public Task PublishAsync(Guid userId, UserEvent userEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
