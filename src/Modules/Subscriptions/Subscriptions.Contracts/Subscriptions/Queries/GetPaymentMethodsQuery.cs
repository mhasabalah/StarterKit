using Subscriptions.Contracts.Subscriptions.DTOs;

namespace Subscriptions.Contracts.Subscriptions.Queries;

public record GetPaymentMethodsQuery : IQuery<IReadOnlyList<PaymentMethodDto>>;

public record GetAdminPaymentMethodsQuery : IQuery<IReadOnlyList<PaymentMethodDto>>;
