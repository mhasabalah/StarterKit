namespace Subscriptions.Domains.OnlinePayments.Features.GetSavedPaymentMethods;

/// <summary>The subscriber's saved payment methods (cards kept for automatic retries). No provider references.</summary>
public record GetSavedPaymentMethodsQuery(Guid SubscriberId) : IQuery<List<SavedPaymentMethodDto>>;

public record SavedPaymentMethodDto(
    Guid Id,
    string Gateway,
    string? Brand,
    string? Last4,
    int? ExpMonth,
    int? ExpYear,
    bool IsDefault,
    bool IsExpired,
    DateTimeOffset CreatedAt);

public class GetSavedPaymentMethodsQueryValidator : AbstractValidator<GetSavedPaymentMethodsQuery>
{
    public GetSavedPaymentMethodsQueryValidator() => RuleFor(x => x.SubscriberId).NotEmpty();
}

public class GetSavedPaymentMethodsHandler(SubscriptionsDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetSavedPaymentMethodsQuery, List<SavedPaymentMethodDto>>
{
    public async Task<List<SavedPaymentMethodDto>> Handle(GetSavedPaymentMethodsQuery query, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var rows = await dbContext.SavedPaymentMethods
            .AsNoTracking()
            .Where(x => x.SubscriberId == query.SubscriberId && x.RevokedAt == null)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => new SavedRow(x.Id, x.Gateway, x.Brand, x.Last4, x.ExpMonth, x.ExpYear, x.IsDefault, x.CreatedAt))
            .ToListAsync(cancellationToken);

        return rows.Select(x => new SavedPaymentMethodDto(
                x.Id, x.Gateway, x.Brand, x.Last4, x.ExpMonth, x.ExpYear, x.IsDefault,
                x.ExpYear is { } year && x.ExpMonth is { } month &&
                new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1) <= now.UtcDateTime,
                x.CreatedAt))
            .ToList();
    }

    private sealed record SavedRow(
        Guid Id,
        string Gateway,
        string? Brand,
        string? Last4,
        int? ExpMonth,
        int? ExpYear,
        bool IsDefault,
        DateTimeOffset CreatedAt);
}
