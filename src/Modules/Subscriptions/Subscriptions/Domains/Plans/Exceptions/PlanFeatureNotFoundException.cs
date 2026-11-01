namespace Subscriptions.Domains.Plans.Exceptions;

public class PlanFeatureNotFoundException(string key)
    : NotFoundException($"Feature with key '{key}' was not found in the plan.");
