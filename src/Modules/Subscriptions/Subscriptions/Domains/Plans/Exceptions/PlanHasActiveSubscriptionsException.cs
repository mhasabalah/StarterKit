namespace Subscriptions.Domains.Plans.Exceptions;

public class PlanHasActiveSubscriptionsException()
    : ConflictException(
        "Plan cannot be deleted because it has active, pending, or past-due subscriptions.");
