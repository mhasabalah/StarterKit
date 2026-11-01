using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetPaymentInstructionsQuery : IQuery<PaymentInstructionsDto?>;
