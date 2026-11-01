namespace Subscriptions.Domains.Subscriptions.Exceptions;

public class SubscriptionNotFoundException(Guid id)
    : NotFoundException($"Subscription '{id}' not found.");
