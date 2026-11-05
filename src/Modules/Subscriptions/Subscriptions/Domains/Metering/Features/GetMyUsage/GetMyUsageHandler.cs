namespace Subscriptions.Domains.Metering.Features.GetMyUsage;

public class GetMyUsageHandler(UsageService usage) : IQueryHandler<GetMyUsageQuery, List<UsageDto>>
{
    public Task<List<UsageDto>> Handle(GetMyUsageQuery query, CancellationToken cancellationToken) =>
        usage.GetPlanUsageAsync(query.SubscriberId, cancellationToken);
}
