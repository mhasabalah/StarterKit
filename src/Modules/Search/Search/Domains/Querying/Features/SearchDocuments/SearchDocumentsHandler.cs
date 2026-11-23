using Microsoft.Extensions.Options;

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
/// Answers a search from the configured engine. Until an engine adapter is registered (G17 step 4) every index reports
/// <see cref="SearchStatus.IndexNotReady"/>, so owners keep using their SQL search.
/// </summary>
public sealed class SearchDocumentsHandler : IQueryHandler<SearchDocumentsQuery, SearchDocumentsResult>
{
    public Task<SearchDocumentsResult> Handle(SearchDocumentsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(SearchDocumentsResult.Unavailable(SearchStatus.IndexNotReady, query.Page, query.PageSize));
}
