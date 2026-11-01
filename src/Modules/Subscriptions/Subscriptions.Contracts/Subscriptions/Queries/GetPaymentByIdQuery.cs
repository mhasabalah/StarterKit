using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetPaymentByIdQuery(Guid PaymentId) : IQuery<AdminPaymentDto?>;
