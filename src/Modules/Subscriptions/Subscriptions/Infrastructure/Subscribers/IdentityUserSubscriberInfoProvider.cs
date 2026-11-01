using Identity.Contracts.Users.Dtos;

namespace Subscriptions.Infrastructure.Subscribers;

/// <summary>
/// Default <see cref="ISubscriberInfoProvider"/>: the subscriber id is an Identity user id.
/// Resolved through the Identity module's public contracts (<see cref="GetUserByIdQuery"/>),
/// never through its internals. Returns <c>null</c> if the user cannot be found so callers
/// can degrade gracefully (e.g. send the email without a personalised name).
/// </summary>
public sealed class IdentityUserSubscriberInfoProvider(
    ISender sender,
    ILogger<IdentityUserSubscriberInfoProvider> logger)
    : ISubscriberInfoProvider
{
    public async Task<SubscriberInfo?> GetAsync(Guid subscriberId, CancellationToken cancellationToken = default)
    {
        UserDto user;
        try
        {
            user = await sender.Send(new GetUserByIdQuery(subscriberId), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve subscriber {SubscriberId} as an Identity user", subscriberId);
            return null;
        }

        var displayName = !string.IsNullOrWhiteSpace(user.FullName)
            ? user.FullName
            : user.UserName;

        return new SubscriberInfo(
            SubscriberId: user.Id,
            DisplayName: displayName,
            Email: user.Email,
            Phone: user.PhoneNumber,
            BillingName: displayName);
    }
}
