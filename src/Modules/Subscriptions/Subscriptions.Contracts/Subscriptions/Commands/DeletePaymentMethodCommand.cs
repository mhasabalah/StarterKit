namespace Subscriptions.Contracts.Subscriptions.Commands;

public record DeletePaymentMethodCommand(Guid Id) : ICommand<Unit>;
