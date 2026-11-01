namespace Subscriptions.Domains.Subscriptions.Exceptions;

public class PaymentNotFoundException(Guid id)
    : NotFoundException($"Payment '{id}' not found.");
