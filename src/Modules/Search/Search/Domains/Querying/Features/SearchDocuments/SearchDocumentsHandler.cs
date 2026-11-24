using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Search.Domains.Analytics.Services;
using Search.Engine.Text;
using Search.Observability;

namespace Search.Domains.Querying.Features.SearchDocuments;

/// <summary>
/// Checks a query against its index definition: the index exists, every filter names a filter field of a fitting
/// type, facets are facetable, the sort is sortable, and paging stays within the configured window.
/// </summary>
public sealed class SearchDocumentsQueryValidator : AbstractValidator<SearchDocumentsQuery>
{
    public const int MaxFilters = 32;
    public const int MaxFacets = 10;
    public const int MaxFilterValues = 100;

    public SearchDocumentsQueryValidator(
        SearchIndexRegistry registry, IOptions<SearchOptions> options, IStringLocalizer<SearchResource> localizer)
    {
        var query = options.Value.Query;

        RuleFor(x => x.IndexKey)
            .Must(key => registry.Find(key) is not null)
            .WithMessage(x => localizer["Validation.IndexKey.Unknown", x.IndexKey]);

        RuleFor(x => x.Text)
            .MaximumLength(query.MaxTextLength)
            .WithMessage(_ => localizer["Validation.Text.MaxLength", query.MaxTextLength]);

        RuleFor(x => x.Mode).IsInEnum().WithMessage(_ => localizer["Validation.Mode.Invalid"]);

        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithMessage(_ => localizer["Validation.Page.Min"]);

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, query.MaxPageSize)
            .WithMessage(_ => localizer["Validation.PageSize.Range", query.MaxPageSize]);

        RuleFor(x => x)
            .Must(x => (long)x.Page * x.PageSize <= query.MaxWindow)
            .When(x => x.Page >= 1 && x.PageSize >= 1)
            .WithName(nameof(SearchDocumentsQuery.Page))
            .WithMessage(_ => localizer["Validation.Window.Max", query.MaxWindow]);

        RuleFor(x => x.Filters.Count)
            .LessThanOrEqualTo(MaxFilters)
            .OverridePropertyName(nameof(SearchDocumentsQuery.Filters))
            .WithMessage(_ => localizer["Validation.Filters.Max", MaxFilters]);

        RuleFor(x => x.Facets.Count)
            .LessThanOrEqualTo(MaxFacets)
            .OverridePropertyName(nameof(SearchDocumentsQuery.Facets))
            .WithMessage(_ => localizer["Validation.Facets.Max", MaxFacets]);

        // Field-level rules need the index: they run only when it exists (the key rule reports otherwise).
        RuleFor(x => x).Custom((x, context) =>
        {
            if (registry.Find(x.IndexKey) is not { } index)
                return;

            foreach (var filter in x.Filters)
            {
                if (FilterProblem(index, filter) is { } problem)
                    context.AddFailure(nameof(SearchDocumentsQuery.Filters), localizer[problem.Key, [.. problem.Args]]);
            }

            foreach (var facet in x.Facets.Distinct(StringComparer.Ordinal))
            {
                if (!index.FilterFields.TryGetValue(facet, out var field) || !field.Facetable)
                    context.AddFailure(nameof(SearchDocumentsQuery.Facets), localizer["Validation.Facet.NotFacetable", facet]);
            }

            if (x.Sort is { } sort && (!index.FilterFields.TryGetValue(sort.Field, out var sortField) || !sortField.Sortable))
                context.AddFailure(nameof(SearchDocumentsQuery.Sort), localizer["Validation.Sort.NotSortable", sort.Field]);
        });
    }

    private static (string Key, object[] Args)? FilterProblem(RegisteredIndex index, SearchFilter filter)
    {
        if (filter?.Field is null || !index.FilterFields.TryGetValue(filter.Field, out var field))
            return ("Validation.Filter.UnknownField", [filter?.Field ?? string.Empty]);

        var keyword = field.Type is SearchFieldType.Keyword or SearchFieldType.KeywordList;
        var wrongType = ("Validation.Filter.WrongType", new object[] { field.Name });

        return filter switch
        {
            KeywordFilter k when !keyword || k.Value is null => wrongType,
            KeywordsFilter k when !keyword || (k.Match == SearchMatch.All && field.Type != SearchFieldType.KeywordList)
                                  || !Enum.IsDefined(k.Match) => wrongType,
            KeywordsFilter k when k.Values is not { Count: >= 1 and <= MaxFilterValues } || k.Values.Any(v => v is null) =>
                ("Validation.Filter.Values", [field.Name, MaxFilterValues]),
            NumberRangeFilter when field.Type != SearchFieldType.Number => wrongType,
            NumberRangeFilter { Min: { } min, Max: { } max } when min > max => ("Validation.Filter.Bounds", [field.Name]),
            DateRangeFilter when field.Type != SearchFieldType.Date => wrongType,
            DateRangeFilter d when d.From is { Kind: not DateTimeKind.Utc } || d.To is { Kind: not DateTimeKind.Utc } =>
                ("Validation.Filter.Utc", [field.Name]),
            DateRangeFilter { From: { } from, To: { } to } when from > to => ("Validation.Filter.Bounds", [field.Name]),
            BoolFilter when field.Type != SearchFieldType.Boolean => wrongType,
            KeywordFilter or KeywordsFilter or NumberRangeFilter or DateRangeFilter or BoolFilter => null,
            _ => wrongType
        };
    }
}

/// <summary>
/// Answers a search from the configured engine, in the caller's tenant and branch scope. An index that was never built
/// answers <see cref="SearchStatus.IndexNotReady"/>, an engine that does not answer
/// <see cref="SearchStatus.EngineUnavailable"/>: in both cases the owner falls back to its SQL search. Highlights are
/// built here, on the original text, the same for every engine. Every answer is counted for the search analytics
/// (<see cref="SearchAnalyticsRecorder"/>; in memory, never delaying the search).
/// </summary>
public sealed class SearchDocumentsHandler(
    SearchIndexRegistry registry,
    SearchDbContext db,
    ISearchEngine engine,
    ITenantContext tenantContext,
    IOptions<SearchOptions> options,
    SearchMetrics metrics,
    TimeProvider time,
    SearchAnalyticsRecorder analytics,
    ILogger<SearchDocumentsHandler> logger)
    : IQueryHandler<SearchDocumentsQuery, SearchDocumentsResult>
{
    public async Task<SearchDocumentsResult> Handle(SearchDocumentsQuery query, CancellationToken cancellationToken)
    {
        var index = registry.Find(query.IndexKey) ?? throw new InvalidOperationException($"Unknown search index '{query.IndexKey}'.");
        if (tenantContext.TenantId is not { } tenantId)
            return SearchDocumentsResult.Unavailable(SearchStatus.IndexNotReady, query.Page, query.PageSize);

        var text = SearchQueryParser.Parse(query.Text, query.Mode, options.Value.Query.MaxTerms);
        var result = await SearchAsync(index, tenantId, query, text, cancellationToken);
        analytics.Record(tenantId, query, text, result);
        return result;
    }

    private async Task<SearchDocumentsResult> SearchAsync(
        RegisteredIndex index, Guid tenantId, SearchDocumentsQuery query, ParsedSearchQuery text, CancellationToken cancellationToken)
    {
        var built = await db.IndexStates.AsNoTracking()
            .Where(s => s.IndexKey == index.Key)
            .Select(s => s.LastBuiltAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (built is null)
            return SearchDocumentsResult.Unavailable(SearchStatus.IndexNotReady, query.Page, query.PageSize);

        var settings = options.Value;
        var engineQuery = new EngineQuery
        {
            Text = text,
            Filters = query.Filters,
            Facets = query.Facets,
            Sort = query.Sort,
            Offset = (query.Page - 1) * query.PageSize,
            Limit = query.PageSize,
            IncludeHighlightText = query.Highlight && !text.IsEmpty,
            FuzzyMinTermLength = query.Fuzzy && settings.Query.Fuzzy.Enabled ? settings.Query.Fuzzy.MinTermLength : null,
            FuzzyThreshold = settings.Query.Fuzzy.Threshold,
            MaxFacetValues = settings.Facets.MaxValues,
            FacetScanLimit = settings.Facets.ScanLimit
        };

        var started = time.GetTimestamp();
        EngineSearchResult result;
        try
        {
            result = await engine.SearchAsync(
                new IndexScope(index, tenantId, tenantContext.Branches.AllowedBranchIds), engineQuery, cancellationToken);
        }
        catch (SearchEngineUnavailableException ex)
        {
            logger.LogWarning(ex, "Search engine {Engine} unavailable for index {IndexKey}; the owner falls back to SQL",
                ex.Engine, index.Key);
            metrics.Queried(index.Key, SearchStatus.EngineUnavailable, SearchMatchMode.Browse, time.GetElapsedTime(started).TotalMilliseconds);
            return SearchDocumentsResult.Unavailable(SearchStatus.EngineUnavailable, query.Page, query.PageSize);
        }

        var matchMode = text.IsEmpty ? SearchMatchMode.Browse : result.MatchMode;
        metrics.Queried(index.Key, SearchStatus.Ok, matchMode, time.GetElapsedTime(started).TotalMilliseconds);

        return new SearchDocumentsResult
        {
            Status = SearchStatus.Ok,
            Hits = result.Hits.Select(hit => new SearchHit(hit.Id, hit.Score, Highlights(hit, text, engineQuery))).ToList(),
            Total = result.Total,
            Facets = result.Facets,
            FacetsExhaustive = result.FacetsExhaustive,
            MatchMode = matchMode,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    private static IReadOnlyDictionary<string, string> Highlights(EngineHit hit, ParsedSearchQuery text, EngineQuery query)
    {
        if (!query.IncludeHighlightText || hit.HighlightText.Count == 0)
            return EmptyHighlights;

        var highlights = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (field, original) in hit.HighlightText)
        {
            if (SearchHighlighter.Highlight(original, text) is { } snippet)
                highlights[field] = snippet;
        }

        return highlights;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyHighlights = new Dictionary<string, string>();
}
