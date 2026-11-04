namespace Subscriptions.Contracts.Subscriptions.Commands;

/// <summary>
/// A subscriber starts an ONLINE payment for what their subscription owes (activation, renewal or upgrade proration):
/// a hosted checkout is created at <paramref name="Provider"/> for the payment's outstanding amount. The subscription
/// is activated/renewed only when the provider's signed webhook reports success.
/// </summary>
/// <param name="Provider">Configured gateway name (<c>Subscriptions:PaymentGateways:&lt;Name&gt;</c>).</param>
/// <param name="PaymentId">The owed payment; optional when exactly one payment is owed.</param>
public record CreateOnlineCheckoutCommand(
    Guid SubscriptionId,
    Guid SubscriberId,
    string Provider,
    string SuccessUrl,
    string CancelUrl,
    Guid? PaymentId = null) : ICommand<CreateOnlineCheckoutResult>;

/// <param name="RedirectUrl">Send the subscriber here to pay.</param>
public record CreateOnlineCheckoutResult(
    Guid SubscriptionId,
    Guid PaymentId,
    string Provider,
    string SessionId,
    string RedirectUrl,
    decimal Amount,
    string Currency);
