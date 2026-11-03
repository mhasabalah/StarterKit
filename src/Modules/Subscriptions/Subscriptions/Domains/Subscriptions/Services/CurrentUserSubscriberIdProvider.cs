using Subscriptions.Contracts.Subscribers;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>Default <see cref="ISubscriberIdProvider"/>: the subscriber is the signed-in user.</summary>
public sealed class CurrentUserSubscriberIdProvider(IHttpContextService httpContextService) : ISubscriberIdProvider
{
    public Guid? GetCurrentSubscriberId() => httpContextService.GetUserIdAsGuid();
}
