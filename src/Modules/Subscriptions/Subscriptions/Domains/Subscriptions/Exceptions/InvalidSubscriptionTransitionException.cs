namespace Subscriptions.Domains.Subscriptions.Exceptions;

public class InvalidSubscriptionTransitionException(SubscriptionStatus from, SubscriptionStatus to)
    : ConflictException($"Cannot transition subscription from {from} to {to}.");
