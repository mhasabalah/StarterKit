using MassTransit;
using Shared.Contracts.Messaging;

namespace Search.Contracts.Indexing.IntegrationEvents;

/// <summary>
/// "These entities of an index changed": raised by the owning aggregate with <c>AddIntegrationEvent</c>, in the same
/// transaction as the change, whenever a searchable value changes (including delete and restore). It carries ids only:
/// Search reads the current state from the index's <see cref="ISearchDocumentSource"/>, so delivery order and
/// repeats do not matter (ADR-0036).
/// </summary>
[MessageUrn("search.documents-changed.v1")]
public sealed class SearchDocumentsChangedIntegrationEvent : IntegrationEvent
{
    /// <summary>The <see cref="SearchIndexDefinition.Key"/> of the index.</summary>
    public required string IndexKey { get; init; }

    /// <summary>The changed entity ids (1 to <see cref="MaxDocuments"/>).</summary>
    public required IReadOnlyList<string> DocumentIds { get; init; }

    public const int MaxDocuments = 500;

    /// <summary>The event for <paramref name="ids"/> of <paramref name="indexKey"/> (duplicates removed).</summary>
    public static SearchDocumentsChangedIntegrationEvent For(string indexKey, params IEnumerable<string> ids)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexKey);
        ArgumentNullException.ThrowIfNull(ids);

        var distinct = ids.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count is 0 or > MaxDocuments)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ids), distinct.Count, $"A search change event carries 1 to {MaxDocuments} ids; split larger changes.");
        }

        if (distinct.FirstOrDefault(id => !SearchDocument.IsValidId(id)) is { } invalid)
            throw new ArgumentException($"'{invalid}' is not a valid search document id.", nameof(ids));

        return new SearchDocumentsChangedIntegrationEvent { IndexKey = indexKey, DocumentIds = distinct };
    }

    /// <summary>The event for entities with Guid ids.</summary>
    public static SearchDocumentsChangedIntegrationEvent For(string indexKey, params IEnumerable<Guid> ids) =>
        For(indexKey, ids.Select(id => id.ToString()));
}
