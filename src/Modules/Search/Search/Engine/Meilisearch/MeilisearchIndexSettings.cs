using System.Text.Json.Nodes;
using static Search.Engine.Meilisearch.MeilisearchNames;

namespace Search.Engine.Meilisearch;

/// <summary>
/// The Meilisearch settings of an index definition (a pure function of the definition and the options, so a changed
/// definition is applied by the reconcile job and a fingerprint change rebuilds the documents).
/// <list type="bullet">
/// <item>Searchable attributes in weight order (Meilisearch's <c>attribute</c> rule ranks earlier attributes first).</item>
/// <item><c>sort</c> is the FIRST ranking rule: a requested sort orders the matches strictly, as the PostgreSQL engine
/// does; without one, relevance (words, typo, proximity, attribute, exactness) decides.</item>
/// <item>Analyzers become <c>localizedAttributes</c> (English, Arabic, both); text arrives already normalized.</item>
/// <item>Facet values by count; the result window and facet sizes follow <see cref="SearchOptions"/>.</item>
/// </list>
/// </summary>
internal static class MeilisearchIndexSettings
{
    public static readonly string[] RankingRules = ["sort", "words", "typo", "proximity", "attribute", "exactness"];

    public static JsonObject Build(RegisteredIndex index, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(options);
        var definition = index.Definition;

        var searchable = definition.TextFields
            .Select((field, order) => (field, order))
            .OrderByDescending(f => f.field.Weight)
            .ThenBy(f => f.order)
            .Select(f => TextAttribute(f.field.Name));

        var filterable = new[] { Tenant, Branch, Stamp, IndexedAt, Id }
            .Concat(definition.FilterFields.Select(f => FilterAttribute(f.Name)));

        var sortable = new[] { Id }
            .Concat(definition.FilterFields.Where(f => f.Sortable).Select(f => FilterAttribute(f.Name)));

        var localized = definition.TextFields
            .Select(f => (Attribute: TextAttribute(f.Name), Locales: Locales(f.Analyzer)))
            .Where(f => f.Locales.Length > 0)
            .Select(f => (JsonNode)new JsonObject
            {
                ["attributePatterns"] = Array([f.Attribute]),
                ["locales"] = Array(f.Locales)
            });

        return new JsonObject
        {
            ["searchableAttributes"] = Array(searchable),
            ["filterableAttributes"] = Array(filterable),
            ["sortableAttributes"] = Array(sortable),
            // Only what the adapter reads back: hits are hydrated by the owner, never from the engine.
            ["displayedAttributes"] = Array([Id, DocumentVersion, Highlight]),
            ["rankingRules"] = Array(RankingRules),
            ["localizedAttributes"] = new JsonArray([.. localized]),
            ["faceting"] = new JsonObject
            {
                ["maxValuesPerFacet"] = options.Facets.MaxValues,
                ["sortFacetValuesBy"] = new JsonObject { ["*"] = "count" }
            },
            ["pagination"] = new JsonObject { ["maxTotalHits"] = Math.Max(options.Query.MaxWindow, options.Facets.ScanLimit) }
        };
    }

    private static string[] Locales(SearchAnalyzer analyzer) => analyzer switch
    {
        SearchAnalyzer.English => ["eng"],
        SearchAnalyzer.Arabic => ["ara"],
        SearchAnalyzer.Bilingual => ["ara", "eng"],
        _ => []
    };

    private static JsonArray Array(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode)v)]);
}
