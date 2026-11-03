namespace Subscriptions.Infrastructure.Subscribers;

/// <summary>
/// Default <see cref="ISubscriberInfoProvider"/>: the subscriber id is a user id, resolved through the provider-neutral
/// user-directory port (<see cref="GetUserProfileQuery"/>) - never through Identity internals, so swapping the identity
/// provider does not touch billing. Returns <c>null</c> if the user is unknown or the directory is unavailable, so
/// callers degrade gracefully (e.g. send the email without a personalised name).
/// </summary>
public sealed class UserDirectorySubscriberInfoProvider(
    ISender sender,
    ILogger<UserDirectorySubscriberInfoProvider> logger)
    : ISubscriberInfoProvider
{
    public async Task<SubscriberInfo?> GetAsync(Guid subscriberId, CancellationToken cancellationToken = default)
    {
        UserProfileDto? user;
        try
        {
            user = await sender.Send(new GetUserProfileQuery(subscriberId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not resolve subscriber {SubscriberId} through the user directory", subscriberId);
            return null;
        }

        if (user is null)
        {
            logger.LogWarning("Subscriber {SubscriberId} is not a known user", subscriberId);
            return null;
        }

        return new SubscriberInfo(
            SubscriberId: user.UserId,
            DisplayName: user.DisplayName,
            Email: user.Email,
            Phone: user.Phone,
            BillingName: user.DisplayName);
    }
}
