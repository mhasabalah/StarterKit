namespace Subscriptions.Domains.Subscriptions.Services;

public static class SubscriptionCacheKeys
{
    private const string SubscriberPrefix = "subscriptions:subscriber:";

    public static string ForSubscriber(Guid subscriberId)
    {
        return $"{SubscriberPrefix}{subscriberId}";
    }
}
