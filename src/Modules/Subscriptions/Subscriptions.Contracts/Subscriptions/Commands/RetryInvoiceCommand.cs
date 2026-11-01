using Shared.Contracts.CQRS;

namespace Subscriptions.Contracts.Subscriptions.Commands;

public record RetryInvoiceCommand(Guid InvoiceId, Guid AdminId) : ICommand<Unit>;
