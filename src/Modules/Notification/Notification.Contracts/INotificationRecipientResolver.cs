namespace Notification.Contracts;

/// <summary>
/// Maps the id passed to <see cref="INotificationSender.For"/> to the USER who receives the notification - for ids
/// that are not users (e.g. a subscriber that is a tenant/organization → its owner). Optional; without an
/// implementation the id is used as the user id.
/// </summary>
public interface INotificationRecipientResolver
{
    /// <returns>The receiving user's id, or <c>null</c> when <paramref name="recipientId"/> is already a user id.</returns>
    Task<Guid?> ResolveUserIdAsync(Guid recipientId, CancellationToken cancellationToken = default);
}
