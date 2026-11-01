namespace Subscriptions.Domains.PaymentSettings.Exceptions;

public class PaymentMethodNotFoundException(Guid id)
    : NotFoundException($"Payment method '{id}' was not found.");
