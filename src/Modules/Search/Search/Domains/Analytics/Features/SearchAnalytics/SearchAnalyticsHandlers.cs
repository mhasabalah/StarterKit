namespace Search.Domains.Analytics.Features.SearchAnalytics;

/// <summary>The day range of an analytics query: the last <see cref="DefaultDays"/> days unless given (UTC, inclusive).</summary>
internal static class SearchAnalyticsRange
{
    public const int DefaultDays = 30;

    public static (DateOnly From, DateOnly To) Resolve(DateOnly? from, DateOnly? to, TimeProvider time)
    {
        var last = to ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return (from ?? last.AddDays(-(DefaultDays - 1)), last);
    }

    /// <summary>The shared rules: a known index (when given), a range in order and at most <see cref="GetSearchDailyStatsQuery.MaxDays"/> days.</summary>
    public static void Rules<T>(
        AbstractValidator<T> validator,
        Func<T, string?> indexKey,
        Func<T, DateOnly?> from,
        Func<T, DateOnly?> to,
        SearchIndexRegistry registry,
        TimeProvider time,
        IStringLocalizer<SearchResource> localizer)
    {
        validator.RuleFor(x => indexKey(x))
            .Must(key => key is null || registry.Find(key) is not null)
            .OverridePropertyName("IndexKey")
            .WithMessage(x => localizer["Validation.IndexKey.Unknown", indexKey(x) ?? string.Empty]);

        validator.RuleFor(x => x).Custom((x, context) =>
        {
            var (first, last) = Resolve(from(x), to(x), time);
            if (first > last)
                context.AddFailure("From", localizer["Validation.Range.Order"]);
            else if (last.DayNumber - first.DayNumber + 1 > GetSearchDailyStatsQuery.MaxDays)
                context.AddFailure("From", localizer["Validation.Range.Max", GetSearchDailyStatsQuery.MaxDays]);
        });
    }
}

public sealed class GetSearchZeroResultTermsQueryValidator : AbstractValidator<GetSearchZeroResultTermsQuery>
{
    public GetSearchZeroResultTermsQueryValidator(
        SearchIndexRegistry registry, TimeProvider time, IStringLocalizer<SearchResource> localizer) =>
        SearchAnalyticsRange.Rules(this, x => x.IndexKey, x => x.From, x => x.To, registry, time, localizer);
}

/// <summary>Zero-result terms summed over the range, most frequent first.</summary>
public sealed class GetSearchZeroResultTermsHandler(SearchDbContext db, TimeProvider time)
    : IQueryHandler<GetSearchZeroResultTermsQuery, PaginatedResult<SearchZeroResultTermDto>>
{
    public Task<PaginatedResult<SearchZeroResultTermDto>> Handle(
        GetSearchZeroResultTermsQuery query, CancellationToken cancellationToken)
    {
        var (from, to) = SearchAnalyticsRange.Resolve(query.From, query.To, time);
        var terms = db.ZeroResultTerms.AsNoTracking().Where(t => t.Day >= from && t.Day <= to);
        if (!string.IsNullOrEmpty(query.IndexKey))
            terms = terms.Where(t => t.IndexKey == query.IndexKey);

        return terms
            .GroupBy(t => new { t.IndexKey, t.Term })
            .Select(g => new { g.Key.IndexKey, g.Key.Term, Count = g.Sum(t => t.Count), LastSeenAt = g.Max(t => t.LastSeenAt) })
            .OrderByDescending(x => x.Count).ThenBy(x => x.IndexKey).ThenBy(x => x.Term)
            .Select(x => new SearchZeroResultTermDto(x.IndexKey, x.Term, x.Count, x.LastSeenAt))
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}

public sealed class GetSearchDailyStatsQueryValidator : AbstractValidator<GetSearchDailyStatsQuery>
{
    public GetSearchDailyStatsQueryValidator(
        SearchIndexRegistry registry, TimeProvider time, IStringLocalizer<SearchResource> localizer) =>
        SearchAnalyticsRange.Rules(this, x => x.IndexKey, x => x.From, x => x.To, registry, time, localizer);
}

/// <summary>One row per index and day that had searches (bounded by the range and the registered indexes).</summary>
public sealed class GetSearchDailyStatsHandler(SearchDbContext db, TimeProvider time)
    : IQueryHandler<GetSearchDailyStatsQuery, List<SearchDailyStatsDto>>
{
    public Task<List<SearchDailyStatsDto>> Handle(GetSearchDailyStatsQuery query, CancellationToken cancellationToken)
    {
        var (from, to) = SearchAnalyticsRange.Resolve(query.From, query.To, time);
        var stats = db.DailyStats.AsNoTracking().Where(s => s.Day >= from && s.Day <= to);
        if (!string.IsNullOrEmpty(query.IndexKey))
            stats = stats.Where(s => s.IndexKey == query.IndexKey);

        return stats
            .OrderBy(s => s.Day).ThenBy(s => s.IndexKey)
            .Select(s => new SearchDailyStatsDto(s.Day, s.IndexKey, s.Searches, s.ZeroResults, s.FuzzyRescues, s.Unanswered))
            .ToListAsync(cancellationToken);
    }
}
