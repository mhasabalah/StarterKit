namespace Subscriptions.Domains.Plans.Exceptions;

public class PlanNotFoundException(Guid planId)
    : NotFoundException($"Plan with ID '{planId}' was not found.");
