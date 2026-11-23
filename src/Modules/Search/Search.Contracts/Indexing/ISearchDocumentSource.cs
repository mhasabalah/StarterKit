namespace Search.Contracts.Indexing;

/// <summary>
/// Where Search reads the current state of an index's entities. Implemented by the owning module (it may inject its
/// own DbContext) and registered with its definition by
/// <see cref="SearchIndexServiceCollectionExtensions.AddSearchIndex{TSource}"/>; resolved per indexing batch, in the
/// tenant scope of the change.
/// </summary>
/// <remarks>
/// Rules for implementations:
/// <list type="bullet">
/// <item>Read every branch of the current tenant (<c>IgnoreBranchScope()</c>): Search applies the reader's branch
/// scope at query time from <see cref="SearchDocument.BranchId"/>.</item>
/// <item>Never return soft-deleted or otherwise retired entities: an entity that is not returned is removed from the
/// index.</item>
/// <item>Read only; never write. Search calls the source again after a failure.</item>
/// </list>
/// </remarks>
public interface ISearchDocumentSource
{
    /// <summary>
    /// The current documents of <paramref name="ids"/>, in any order. An id without a document is deleted from the
    /// index (the entity was deleted, or is no longer searchable).
    /// </summary>
    Task<IReadOnlyList<SearchDocument>> GetAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken);

    /// <summary>Every searchable entity of the current tenant, streamed (an index rebuild).</summary>
    IAsyncEnumerable<SearchDocument> StreamAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The id and version of every searchable entity of the current tenant (drift detection). The default projects
    /// <see cref="StreamAllAsync"/>; override it with a cheaper query of the two columns.
    /// </summary>
    IAsyncEnumerable<SearchDocumentStamp> StreamStampsAsync(CancellationToken cancellationToken) =>
        SearchDocumentStamp.From(StreamAllAsync(cancellationToken), cancellationToken);
}

/// <summary>The identity and version of an indexed entity, compared by drift detection.</summary>
public sealed record SearchDocumentStamp(string Id, long Version)
{
    internal static async IAsyncEnumerable<SearchDocumentStamp> From(
        IAsyncEnumerable<SearchDocument> documents,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var document in documents.WithCancellation(cancellationToken))
            yield return new SearchDocumentStamp(document.Id, document.Version);
    }
}
