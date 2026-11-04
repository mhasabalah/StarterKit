using Subscriptions.Contracts.Subscriptions.DTOs;
using Subscriptions.Contracts.Subscriptions.Queries;
using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Domains.OnlinePayments.Features.GetFailedWebhookEvents;

public class GetFailedWebhookEventsQueryValidator : AbstractValidator<GetFailedWebhookEventsQuery>
{
    public GetFailedWebhookEventsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public class GetFailedWebhookEventsHandler(SubscriptionsDbContext dbContext)
    : IQueryHandler<GetFailedWebhookEventsQuery, PaginatedResult<InboundWebhookEventDto>>
{
    public async Task<PaginatedResult<InboundWebhookEventDto>> Handle(
        GetFailedWebhookEventsQuery query,
        CancellationToken cancellationToken)
    {
        var events = dbContext.InboundWebhookEvents
            .AsNoTracking()
            .Where(x => x.Status == InboundWebhookStatus.Failed);

        if (!string.IsNullOrWhiteSpace(query.Provider))
            events = events.Where(x => x.Provider == query.Provider);

        return await events
            .OrderByDescending(x => x.ReceivedAt)
            .Select(x => new InboundWebhookEventDto(
                x.Id, x.Provider, x.EventId, x.Type, x.ProviderReference, x.Amount, x.Currency,
                x.ReceivedAt, x.LastAttemptAt, x.Attempts, x.LastError))
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}
