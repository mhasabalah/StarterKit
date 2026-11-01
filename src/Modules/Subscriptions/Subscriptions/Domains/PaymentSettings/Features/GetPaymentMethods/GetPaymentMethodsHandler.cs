using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;

namespace Subscriptions.Domains.PaymentSettings.Features.GetPaymentMethods;

public class GetPaymentMethodsHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetPaymentMethodsQuery, IReadOnlyList<PaymentMethodDto>>
{
    public async Task<IReadOnlyList<PaymentMethodDto>> Handle(
        GetPaymentMethodsQuery query,
        CancellationToken cancellationToken)
    {
        return await dbContext.PaymentMethods
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new PaymentMethodDto(
                x.Id,
                x.Type,
                x.DisplayName,
                x.Value,
                x.Description,
                x.Metadata,
                x.SortOrder,
                x.IsActive,
                x.Version))
            .ToListAsync(cancellationToken);
    }
}

public class GetAdminPaymentMethodsHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetAdminPaymentMethodsQuery, IReadOnlyList<PaymentMethodDto>>
{
    public async Task<IReadOnlyList<PaymentMethodDto>> Handle(
        GetAdminPaymentMethodsQuery query,
        CancellationToken cancellationToken)
    {
        return await dbContext.PaymentMethods
            .AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .Select(x => new PaymentMethodDto(
                x.Id,
                x.Type,
                x.DisplayName,
                x.Value,
                x.Description,
                x.Metadata,
                x.SortOrder,
                x.IsActive,
                x.Version))
            .ToListAsync(cancellationToken);
    }
}
