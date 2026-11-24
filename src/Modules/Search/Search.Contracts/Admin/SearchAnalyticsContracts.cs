namespace Search.Contracts.Admin;

/// <summary>
/// The searches of the CURRENT tenant that found nothing, most frequent first, over a range of days (default: the last
/// 30). The text is normalized (case, accents, Arabic letter forms) and never tied to a user; searches that look like an
/// email address or a long number are not kept. Tenant permission <c>search.manage</c>.
/// </summary>
public sealed record GetSearchZeroResultTermsQuery : BaseQuery, IQuery<PaginatedResult<SearchZeroResultTermDto>>
{
    /// <summary>One index; all of them when empty.</summary>
    public string? IndexKey { get; init; }

    /// <summary>First day (UTC), inclusive.</summary>
    public DateOnly? From { get; init; }

    /// <summary>Last day (UTC), inclusive.</summary>
    public DateOnly? To { get; init; }
}

public sealed record SearchZeroResultTermDto(string IndexKey, string Term, int Count, DateTime LastSeenAt);

/// <summary>
/// Daily search counts of the CURRENT tenant per index, over a range of days (default: the last 30, at most
/// <see cref="MaxDays"/>). Only full searches with text are counted (not autocomplete, not later pages).
/// </summary>
public sealed record GetSearchDailyStatsQuery : IQuery<List<SearchDailyStatsDto>>
{
    public const int MaxDays = 366;

    public string? IndexKey { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }
}

/// <param name="ZeroResults">Searches the index answered with nothing.</param>
/// <param name="FuzzyRescues">Searches that matched nothing exactly but found close spellings.</param>
/// <param name="Unanswered">Searches the index could not answer (not built yet, engine down): the owner's SQL search answered them.</param>
public sealed record SearchDailyStatsDto(
    DateOnly Day, string IndexKey, int Searches, int ZeroResults, int FuzzyRescues, int Unanswered)
{
    /// <summary>Share of the answered searches that found nothing, 0 to 1.</summary>
    public double ZeroResultRate => Searches - Unanswered is > 0 and var answered ? Math.Round((double)ZeroResults / answered, 4) : 0;
}
