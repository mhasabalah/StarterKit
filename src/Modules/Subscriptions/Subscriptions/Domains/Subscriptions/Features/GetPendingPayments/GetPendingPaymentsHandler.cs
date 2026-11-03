using Subscriptions.Domains.Subscriptions.Services;

namespace Subscriptions.Domains.Subscriptions.Features.GetPendingPayments;

public class GetPendingPaymentsQueryValidator : AbstractValidator<GetPendingPaymentsQuery>
{
    public GetPendingPaymentsQueryValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100.");

        RuleFor(x => x.Purpose)
            .Must(x => x is null || Enum.TryParse<PaymentPurpose>(x, ignoreCase: true, out _))
            .WithMessage("Purpose must be Activation, Renewal or Proration.");

        RuleFor(x => x)
            .Must(x => x.MinAmount is null || x.MaxAmount is null || x.MinAmount <= x.MaxAmount)
            .WithMessage("MinAmount must not exceed MaxAmount.");

        RuleFor(x => x)
            .Must(x => x.SubmittedFrom is null || x.SubmittedTo is null || x.SubmittedFrom < x.SubmittedTo)
            .WithMessage("SubmittedFrom must be before SubmittedTo.");

        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Method).MaximumLength(100);
    }
}

/// <summary>
/// The proof-review queue: submitted payments with subscriber, plan, amounts, age and the duplicate-proof flag.
/// Filters and paging run in SQL; only the age is computed after paging.
/// </summary>
public class GetPendingPaymentsHandler(SubscriptionsDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetPendingPaymentsQuery, PaginatedResult<PendingPaymentDto>>
{
    public async Task<PaginatedResult<PendingPaymentDto>> Handle(
        GetPendingPaymentsQuery query,
        CancellationToken cancellationToken)
    {
        var queryable = ApplyFilters(
            dbContext.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Submitted),
            query);

        queryable = queryable.OrderBy(p => p.SubmittedAt ?? p.CreatedAt);
        queryable = queryable.OrderByDynamic(query.SortColumn, query.SortBy);

        var dtoQuery = queryable.Select(p => new PendingPaymentDto(
            p.Id,
            p.SubscriptionId,
            p.Subscription.SubscriberId,
            p.Amount,
            p.Currency,
            new PaymentMethodSnapshotDto(
                p.PaymentMethodId,
                p.PaymentMethodType,
                p.PaymentMethodDisplayName,
                p.PaymentMethodValue,
                p.PaymentMethodDescription),
            p.SubmittedAt ?? p.CreatedAt,
            "use /proof endpoint",
            p.Purpose.ToString(),
            p.Version,
            p.Subscription.Version,
            p.Subscription.SubscriberDisplayName,
            p.Subscription.SubscriberEmail,
            p.Subscription.PlanId,
            p.Subscription.PlanName,
            0d,
            p.DuplicateProof,
            p.DuplicateOfPaymentId,
            p.ProofAttachmentId,
            p.AmountReceived,
            p.Amount - p.AmountReceived));

        var page = await dtoQuery.PagedResult(query.PageNumber, query.PageSize, cancellationToken);

        var now = timeProvider.GetUtcNow();
        page.Result = page.Result?
            .Select(x => x with { AgeHours = BillingDashboardCalculator.AgeHours(x.SubmittedAt, now) ?? 0d })
            .ToList();

        return page;
    }

    private static IQueryable<SubscriptionPayment> ApplyFilters(IQueryable<SubscriptionPayment> queryable, GetPendingPaymentsQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Method))
        {
            var method = query.Method.Trim();
            queryable = queryable.Where(p =>
                EF.Functions.ILike(p.PaymentMethodType ?? string.Empty, method) ||
                EF.Functions.ILike(p.PaymentMethodDisplayName ?? string.Empty, method));
        }

        if (query.PaymentMethodId.HasValue)
            queryable = queryable.Where(p => p.PaymentMethodId == query.PaymentMethodId.Value);

        if (!string.IsNullOrWhiteSpace(query.Purpose) &&
            Enum.TryParse<PaymentPurpose>(query.Purpose, ignoreCase: true, out var purpose))
        {
            queryable = queryable.Where(p => p.Purpose == purpose);
        }

        // timestamptz parameters must be UTC (offset 0) - normalize client-supplied offsets.
        if (query.SubmittedFrom.AsUtc() is { } submittedFrom)
            queryable = queryable.Where(p => (p.SubmittedAt ?? p.CreatedAt) >= submittedFrom);

        if (query.SubmittedTo.AsUtc() is { } submittedTo)
            queryable = queryable.Where(p => (p.SubmittedAt ?? p.CreatedAt) < submittedTo);

        if (query.MinAmount.HasValue)
            queryable = queryable.Where(p => p.Amount >= query.MinAmount.Value);

        if (query.MaxAmount.HasValue)
            queryable = queryable.Where(p => p.Amount <= query.MaxAmount.Value);

        if (query.DuplicateProofOnly == true)
            queryable = queryable.Where(p => p.DuplicateProof);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            if (Guid.TryParse(query.Search.Trim(), out var id))
            {
                queryable = queryable.Where(p =>
                    p.Id == id || p.SubscriptionId == id || p.Subscription.SubscriberId == id);
            }
            else
            {
                var pattern = AdminBillingProjections.ContainsPattern(query.Search);
                queryable = queryable.Where(p =>
                    EF.Functions.ILike(p.Subscription.SubscriberDisplayName ?? string.Empty, pattern, AdminBillingProjections.LikeEscape) ||
                    EF.Functions.ILike(p.Subscription.SubscriberEmail ?? string.Empty, pattern, AdminBillingProjections.LikeEscape) ||
                    EF.Functions.ILike(p.Subscription.PlanName, pattern, AdminBillingProjections.LikeEscape));
            }
        }

        return queryable;
    }
}
