using Microsoft.Extensions.DependencyInjection;

namespace Agentic.Contracts.Knowledge;

/// <summary>
/// A kind of content a module makes searchable for agents (e.g. <c>shipping.documents</c>). Registered in code by the
/// module that owns the content, with <see cref="KnowledgeServiceCollectionExtensions.AddKnowledgeSource"/>.
/// <para>
/// Only what a module explicitly indexes is searchable, and a search returns a passage only when the user the run acts
/// for may read its source (<see cref="RequiredPermission"/>) and, when the module registered an
/// <see cref="IKnowledgeSourcePolicy"/>, the document itself.
/// </para>
/// </summary>
public sealed record KnowledgeSourceDefinition
{
    /// <summary>Stable key <c>&lt;module&gt;.&lt;content&gt;</c> in lower case, e.g. <c>shipping.documents</c>.</summary>
    public required string Key { get; init; }

    public required LocalizedText Name { get; init; }

    /// <summary>
    /// Tenant permission the user a run acts for must hold to get passages of this source. Exactly one of this and
    /// <see cref="AllowAnyMember"/> (checked at startup).
    /// </summary>
    public string? RequiredPermission { get; init; }

    /// <summary>Explicit opt-out of <see cref="RequiredPermission"/>: every member of the tenant may read the source.</summary>
    public bool AllowAnyMember { get; init; }
}

/// <summary>
/// Document-level access check of one source, implemented by the module that owns it (optional). It runs as the user
/// the run acts for, inside their tenant and branch scope, so it can use the module's own queries.
/// </summary>
public interface IKnowledgeSourcePolicy
{
    /// <summary>The <see cref="KnowledgeSourceDefinition.Key"/> this policy guards.</summary>
    string SourceKey { get; }

    /// <summary>The subset of <paramref name="externalRefs"/> the current user may read. Never throw for a ref they may not read.</summary>
    Task<IReadOnlySet<string>> GetReadableAsync(IReadOnlyCollection<string> externalRefs, CancellationToken cancellationToken);
}

public static class KnowledgeServiceCollectionExtensions
{
    /// <summary>Registers a knowledge source. Keys must be unique (checked at startup).</summary>
    public static IServiceCollection AddKnowledgeSource(this IServiceCollection services, KnowledgeSourceDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        services.AddSingleton(source);
        return services;
    }

    /// <summary>Registers the document-level policy of a source (scoped: resolved per search).</summary>
    public static IServiceCollection AddKnowledgeSourcePolicy<TPolicy>(this IServiceCollection services)
        where TPolicy : class, IKnowledgeSourcePolicy
    {
        services.AddScoped<IKnowledgeSourcePolicy, TPolicy>();
        return services;
    }
}

public enum KnowledgeDocumentStatus
{
    /// <summary>Waiting for (re)indexing.</summary>
    [LocalizedDescription("Pending", "قيد الانتظار")]
    Pending = 1,

    /// <summary>Searchable.</summary>
    [LocalizedDescription("Ready", "جاهز")]
    Ready = 2,

    /// <summary>Could not be indexed (see the failure code); the previous revision is no longer searchable.</summary>
    [LocalizedDescription("Failed", "فشل")]
    Failed = 3
}

/// <summary>
/// Makes one document of a registered source searchable, or replaces it (trusted: sent by the owning module, never
/// bound to an endpoint). Give EITHER <paramref name="Text"/> OR <paramref name="AttachmentId"/> (a Ready attachment;
/// plain text, Markdown, CSV, JSON, HTML and PDF with a text layer). Indexing happens in the background; sending the
/// same content again is a no-op, so the command is safe to repeat.
/// </summary>
/// <param name="SourceKey">A registered <see cref="KnowledgeSourceDefinition.Key"/>.</param>
/// <param name="ExternalRef">The owning module's id of the document (unique per source and tenant).</param>
/// <param name="Title">Shown in citations.</param>
public sealed record IndexKnowledgeDocumentCommand(
    string SourceKey,
    string ExternalRef,
    string Title,
    string? Text = null,
    Guid? AttachmentId = null) : ICommand<IndexKnowledgeDocumentResult>;

/// <param name="Queued">False when nothing changed (same content) or knowledge is switched off.</param>
public sealed record IndexKnowledgeDocumentResult(Guid? DocumentId, int Revision, bool Queued);

/// <summary>Removes a document and its passages at once (trusted, owning module). Idempotent.</summary>
public sealed record RemoveKnowledgeDocumentCommand(string SourceKey, string ExternalRef) : ICommand<bool>;

public sealed record KnowledgeDocumentDto(
    Guid Id,
    string SourceKey,
    string ExternalRef,
    string Title,
    Guid? AttachmentId,
    KnowledgeDocumentStatus Status,
    int Revision,
    int ChunkCount,
    string? EmbeddingModel,
    string? FailureCode,
    DateTime? IndexedAt,
    DateTime CreatedAt);

/// <summary>The tenant's indexed documents (operators; content is not returned).</summary>
public sealed record GetKnowledgeDocumentsQuery(
    string? SourceKey = null,
    KnowledgeDocumentStatus? Status = null,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PaginatedResult<KnowledgeDocumentDto>>;
