using System.Text.Json.Nodes;
using static Search.Engine.Meilisearch.MeilisearchNames;

namespace Search.Engine.Meilisearch;

/// <summary>Prepared documents as Meilisearch documents (see <see cref="MeilisearchNames"/> for the layout).</summary>
internal static class MeilisearchDocuments
{
    /// <summary>
    /// One document per id: within a batch the highest version wins (Meilisearch applies a batch in order, and has no
    /// conditional write to guard versions across batches).
    /// </summary>
    public static IReadOnlyList<EngineDocument> LatestPerId(IEnumerable<EngineDocument> documents) =>
        documents.GroupBy(d => d.Id, StringComparer.Ordinal)
            .Select(g => g.Aggregate((latest, next) => next.Version >= latest.Version ? next : latest))
            .ToList();

    public static JsonObject ToJson(Guid tenantId, EngineDocument document, long stamp, DateTime indexedAt)
    {
        var json = new JsonObject
        {
            [Key] = DocumentKey(tenantId, document.Id),
            [Id] = document.Id,
            [Tenant] = TenantValue(tenantId),
            // Always present: the branch filter needs an explicit null ("tenant-wide") to match IS NULL.
            [Branch] = document.BranchId?.ToString(),
            [DocumentVersion] = document.Version,
            [Stamp] = stamp,
            [IndexedAt] = EpochMilliseconds(indexedAt)
        };

        foreach (var (field, text) in document.Text)
            json[TextAttribute(field)] = text;

        foreach (var (field, value) in document.Filters)
            json[FilterAttribute(field)] = ToJson(value);

        if (document.HighlightOriginals.Count > 0)
        {
            var originals = new JsonObject();
            foreach (var (field, original) in document.HighlightOriginals)
                originals[field] = original;
            json[Highlight] = originals;
        }

        return json;
    }

    public static long EpochMilliseconds(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private static JsonNode ToJson(SearchValue value) => value switch
    {
        KeywordValue k => k.Value,
        KeywordListValue l => new JsonArray([.. l.Values.Select(v => (JsonNode)v)]),
        NumberValue n => n.Value,
        DateValue d => EpochMilliseconds(d.Value),
        BooleanValue b => b.Value,
        _ => throw new InvalidOperationException($"Unsupported search value {value.GetType().Name}.")
    };
}
