namespace Subscriptions.Domains.Subscriptions.Exceptions;

/// <summary>Plan changes must keep the currency and billing cycle (no cross-currency or cross-cycle proration).</summary>
public class PlanChangeNotSupportedException()
    : BadRequestException("Plan changes across currencies or billing cycles are not supported.");
