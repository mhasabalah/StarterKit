namespace Subscriptions.Domains.Subscriptions.Exceptions;

public class ActiveSubscriptionAlreadyExistsException()
    : ConflictException("Subscriber already has an active or pending subscription.");
