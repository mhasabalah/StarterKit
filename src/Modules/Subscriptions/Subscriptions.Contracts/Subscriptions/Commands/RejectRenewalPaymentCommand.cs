namespace Subscriptions.Contracts.Subscriptions.Commands;

public record RejectRenewalPaymentCommand(Guid SubscriptionId, Guid PaymentId, Guid AdminId, string Reason)
    : ICommand;
