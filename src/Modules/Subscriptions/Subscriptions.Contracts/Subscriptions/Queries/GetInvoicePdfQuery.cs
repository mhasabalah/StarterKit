using Shared.Contracts.CQRS;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetInvoicePdfQuery(
    Guid InvoiceId,
    Guid ActorId,
    bool IsAdmin) : IQuery<(string FileUrl, string ContentType, string FileName)>;
