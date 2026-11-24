using System.Text.Json.Nodes;
using static Search.Engine.Meilisearch.MeilisearchNames;

namespace Search.Engine.Meilisearch;

/// <summary>
/// One search as a Meilisearch request, and its response as an <see cref="EngineSearchResult"/>. A single <c>q</c> is a
/// plain index search; several (OR alternatives, see <see cref="MeilisearchQueryText"/>) are one federated multi-search,
/// which merges and de-duplicates the hits.
/// </summary>
internal static class MeilisearchSearches
{
    public static JsonObject Single(string q, string filter, EngineQuery query, RegisteredIndex index)
    {
        var body = Query(q, filter, query, index);
        if (query.Facets.Count > 0)
            body["facets"] = Strings(query.Facets.Select(FilterAttribute));

        // page/hitsPerPage gives an exact total (up to the index's maxTotalHits); offset/limit only an estimate.
        if (query.Limit > 0 && query.Offset % query.Limit == 0)
        {
            body["page"] = query.Offset / query.Limit + 1;
            body["hitsPerPage"] = query.Limit;
        }
        else
        {
            body["offset"] = query.Offset;
            body["limit"] = query.Limit;
        }

        return body;
    }

    public static JsonObject Federated(string uid, IReadOnlyList<string> qs, string filter, EngineQuery query, RegisteredIndex index)
    {
        var federation = new JsonObject { ["offset"] = query.Offset, ["limit"] = query.Limit };
        if (query.Facets.Count > 0)
        {
            federation["facetsByIndex"] = new JsonObject { [uid] = Strings(query.Facets.Select(FilterAttribute)) };
            federation["mergeFacets"] = new JsonObject { ["maxValuesPerFacet"] = query.MaxFacetValues };
        }

        var queries = qs.Select(q =>
        {
            var single = Query(q, filter, query, index);
            single["indexUid"] = uid;
            return (JsonNode)single;
        });

        return new JsonObject { ["federation"] = federation, ["queries"] = new JsonArray([.. queries]) };
    }

    /// <summary>
    /// The hits in the engine's order, the total, and the requested facets in request order (most frequent values
    /// first, ties by value, at most <see cref="EngineQuery.MaxFacetValues"/>).
    /// </summary>
    /// <param name="exhaustiveFacets">
    /// <c>false</c> for a federated search: each alternative counts its own matches, so a document matching two
    /// alternatives is counted twice.
    /// </param>
    public static EngineSearchResult Read(JsonNode response, EngineQuery query, SearchMatchMode mode, bool exhaustiveFacets)
    {
        var hits = (response["hits"]?.AsArray() ?? [])
            .Where(hit => hit is not null)
            .Select(hit => new EngineHit(
                hit!["_id"]!.GetValue<string>(),
                hit[RankingScore]?.GetValue<double>() ?? 0,
                query.IncludeHighlightText ? Originals(hit[Highlight]) : NoText))
            .ToList();

        var total = (response["totalHits"] ?? response["estimatedTotalHits"])?.GetValue<int>() ?? hits.Count;
        var distribution = response["facetDistribution"]?.AsObject();
        var facets = query.Facets
            .Select(field => new SearchFacet(field, Values(distribution?[FilterAttribute(field)]?.AsObject(), query.MaxFacetValues)))
            .ToList();

        return new EngineSearchResult(hits, total, facets, exhaustiveFacets, mode);
    }

    /// <summary>
    /// The requested sort (then id), or without text the index's default sort (then id), or plain relevance. With
    /// <c>sort</c> as the first ranking rule, a sort orders the matches strictly, like the PostgreSQL engine.
    /// </summary>
    public static IReadOnlyList<string> Sort(EngineQuery query, RegisteredIndex index)
    {
        var sort = query.Sort ?? (query.Text.IsEmpty ? index.Definition.DefaultSort : null);
        if (sort is null)
            return query.Text.IsEmpty ? [$"{Id}:asc"] : [];
        return [$"{FilterAttribute(sort.Field)}:{(sort.Descending ? "desc" : "asc")}", $"{Id}:asc"];
    }

    private static JsonObject Query(string q, string filter, EngineQuery query, RegisteredIndex index)
    {
        var body = new JsonObject
        {
            ["q"] = q,
            ["matchingStrategy"] = "all",
            ["filter"] = filter,
            ["showRankingScore"] = true,
            ["attributesToRetrieve"] = Strings(query.IncludeHighlightText ? [Id, Highlight] : [Id])
        };

        var sort = Sort(query, index);
        if (sort.Count > 0)
            body["sort"] = Strings(sort);
        return body;
    }

    private static List<SearchFacetValue> Values(JsonObject? counts, int max) =>
        counts is null
            ? []
            : counts.Select(c => new SearchFacetValue(c.Key, c.Value!.GetValue<int>()))
                .OrderByDescending(v => v.Count)
                .ThenBy(v => v.Value, StringComparer.Ordinal)
                .Take(max)
                .ToList();

    private static IReadOnlyDictionary<string, string> Originals(JsonNode? highlight) =>
        highlight is JsonObject fields
            ? fields.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value!.GetValue<string>(), StringComparer.Ordinal)
            : NoText;

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode)v)]);

    private static readonly IReadOnlyDictionary<string, string> NoText = new Dictionary<string, string>();
}
