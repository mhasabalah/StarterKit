namespace Subscriptions.Domains.Subscriptions.Exceptions;

public class PaymentAmountMismatchException(
    Guid subscriptionId,
    decimal expectedAmount,
    string expectedCurrency,
    decimal actualAmount,
    string actualCurrency)
    : ConflictException(
        $"Payment amount mismatch for subscription '{subscriptionId}'. " +
        $"Expected {expectedAmount} {expectedCurrency}, received {actualAmount} {actualCurrency}.");
