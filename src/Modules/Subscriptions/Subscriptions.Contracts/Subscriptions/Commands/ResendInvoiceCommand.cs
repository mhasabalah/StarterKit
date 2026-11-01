using Shared.Contracts.CQRS;

namespace Subscriptions.Contracts.Subscriptions.Commands;

public record ResendInvoiceCommand(Guid InvoiceId, Guid AdminId) : ICommand<Unit>;
