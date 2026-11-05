using Hangfire;
using Webhooks.Domains.Deliveries.Jobs;
using Webhooks.Domains.Deliveries.Models;
using Webhooks.Domains.Endpoints.Exceptions;

namespace Webhooks.Domains.Deliveries.Features;

public sealed class WebhookDeliveryNotFoundException(Guid id) : NotFoundException("WebhookDelivery", id);

// ----- list -----------------------------------------------------------------------------------------------------

public class GetWebhookDeliveriesQueryValidator : AbstractValidator<GetWebhookDeliveriesQuery>
{
    public GetWebhookDeliveriesQueryValidator(IStringLocalizer<WebhooksResource> localizer)
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => localizer["Validation.PageSize.Range"])
            .When(x => x.PageSize.HasValue);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.EventType).MaximumLength(WebhookDelivery.EventTypeMaxLength);
    }
}

public class GetWebhookDeliveriesHandler(WebhooksDbContext dbContext)
    : IQueryHandler<GetWebhookDeliveriesQuery, PaginatedResult<WebhookDeliveryDto>>
{
    public Task<PaginatedResult<WebhookDeliveryDto>> Handle(GetWebhookDeliveriesQuery query,
        CancellationToken cancellationToken)
    {
        var deliveries = dbContext.Deliveries.AsNoTracking();
        if (query.EndpointId is { } endpointId)
            deliveries = deliveries.Where(d => d.EndpointId == endpointId);
        if (query.Status is { } status)
            deliveries = deliveries.Where(d => d.Status == status);
        if (!string.IsNullOrWhiteSpace(query.EventType))
            deliveries = deliveries.Where(d => d.EventType == query.EventType);

        return deliveries
            .OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id)
            .Select(d => new WebhookDeliveryDto(d.Id, d.EndpointId, d.EventId, d.EventType, d.Status, d.AttemptCount,
                d.LastResponseCode, d.LastDurationMs, d.LastError, d.NextAttemptAt, d.LastAttemptAt, d.DeliveredAt,
                d.RedeliveryOfId, d.CreatedAt ?? DateTime.MinValue))
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}

// ----- detail ---------------------------------------------------------------------------------------------------

public class GetWebhookDeliveryByIdHandler(WebhooksDbContext dbContext)
    : IQueryHandler<GetWebhookDeliveryByIdQuery, WebhookDeliveryDetailDto>
{
    public async Task<WebhookDeliveryDetailDto> Handle(GetWebhookDeliveryByIdQuery query, CancellationToken cancellationToken) =>
        await dbContext.Deliveries.AsNoTracking()
            .Where(d => d.Id == query.Id)
            .Select(d => new WebhookDeliveryDetailDto(d.Id, d.EndpointId, d.EventId, d.EventType, d.Status, d.AttemptCount,
                d.LastResponseCode, d.LastDurationMs, d.LastError, d.LastResponseSnippet, d.NextAttemptAt, d.LastAttemptAt,
                d.DeliveredAt, d.RedeliveryOfId, d.CreatedAt ?? DateTime.MinValue, d.Payload))
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new WebhookDeliveryNotFoundException(query.Id);
}

// ----- redeliver ------------------------------------------------------------------------------------------------

public class RedeliverWebhookCommandValidator : AbstractValidator<RedeliverWebhookCommand>
{
    public RedeliverWebhookCommandValidator(IStringLocalizer<WebhooksResource> localizer) =>
        RuleFor(x => x.DeliveryId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

/// <summary>
/// Queues a NEW delivery of the same event (same <c>webhook-id</c>, same payload) to the same endpoint. The original keeps its
/// history. The endpoint must be enabled: re-enable it first after an automatic disable.
/// </summary>
public class RedeliverWebhookHandler(
    IWebhookDeliveryRepository deliveries,
    IWebhookEndpointRepository endpoints,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider)
    : ICommandHandler<RedeliverWebhookCommand, RedeliverWebhookResult>
{
    public async Task<RedeliverWebhookResult> Handle(RedeliverWebhookCommand command, CancellationToken cancellationToken)
    {
        var original = await deliveries.GetByIdAsync(command.DeliveryId, cancellationToken)
                       ?? throw new WebhookDeliveryNotFoundException(command.DeliveryId);

        if (original.Status == WebhookDeliveryStatus.Pending)
            throw new ConflictException("The delivery is still pending; wait for it to finish before redelivering.");

        var endpoint = await endpoints.GetByIdAsync(original.EndpointId, cancellationToken)
                       ?? throw new WebhookEndpointNotFoundException(original.EndpointId);
        if (!endpoint.Enabled)
            throw new InvalidWebhookEndpointException("The endpoint is disabled; enable it before redelivering.");

        var repeat = original.Redeliver(timeProvider.GetUtcNow().UtcDateTime);
        await deliveries.AddAsync(repeat, cancellationToken);
        await deliveries.SaveChangesAsync(cancellationToken);

        var id = repeat.Id;
        jobs.Enqueue<DeliverWebhookJob>(job => job.ExecuteAsync(id, CancellationToken.None));
        return new RedeliverWebhookResult(id);
    }
}
