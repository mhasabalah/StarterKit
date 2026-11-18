using System.Security.Cryptography;
using System.Text;
using Agentic.Contracts.Knowledge;

namespace Agentic.Domains.Knowledge.Models;

/// <summary>
/// A document a module made searchable (<c>IndexKnowledgeDocumentCommand</c>), identified by its source and the owning
/// module's id. Every change of content is a new <see cref="Revision"/>; the indexing job works on one revision and its
/// result is accepted only while that revision is still current, so a slow job never overwrites newer content.
/// <para>
/// The passages (<see cref="KnowledgeChunk"/>) are separate rows written in bulk by the indexing job; this row is the
/// state and the concurrency boundary.
/// </para>
/// </summary>
public class KnowledgeDocument : Aggregate<Guid>, IMustHaveTenant
{
    public const int SourceKeyMaxLength = 100;
    public const int ExternalRefMaxLength = 200;
    public const int TitleMaxLength = 300;
    public const int FailureCodeMaxLength = 100;
    public const int EmbeddingModelMaxLength = 200;
    public const int ContentHashLength = 64;

    public Guid TenantId { get; set; }

    public string SourceKey { get; private set; } = default!;
    public string ExternalRef { get; private set; } = default!;
    public string Title { get; private set; } = default!;

    /// <summary>The attachment to read, when the content is a file.</summary>
    public Guid? AttachmentId { get; private set; }

    /// <summary>The content, when it was given as text (kept so the document can be indexed again).</summary>
    public string? SourceText { get; private set; }

    /// <summary>SHA-256 of <see cref="SourceText"/> (hex), so sending the same text again changes nothing.</summary>
    public string? ContentHash { get; private set; }

    public KnowledgeDocumentStatus Status { get; private set; }

    /// <summary>Raised by every change of content; chunks carry the revision they were built from.</summary>
    public int Revision { get; private set; }

    /// <summary>
    /// The revision whose passages are searchable now (null: none). A changed document keeps serving its last indexed
    /// passages until the new revision's passages replace them in one transaction.
    /// </summary>
    public int? IndexedRevision { get; private set; }

    public int ChunkCount { get; private set; }

    /// <summary>Identity of the embedding model of the current chunks (vectors of another model are never compared).</summary>
    public string? EmbeddingModel { get; private set; }

    public string? FailureCode { get; private set; }

    /// <summary>Indexing attempts of the current revision.</summary>
    public int Attempts { get; private set; }

    public DateTime? IndexedAt { get; private set; }
    public DateTime? LastEnqueuedAt { get; private set; }

    public new DateTime CreatedAt { get; private set; }

    private KnowledgeDocument()
    {
    }

    public static KnowledgeDocument Create(string sourceKey, string externalRef, string title, string? text,
        Guid? attachmentId, DateTime now)
    {
        if ((text is null) == (attachmentId is null))
            throw new ArgumentException("A knowledge document has either text or an attachment.");

        return new KnowledgeDocument
        {
            Id = Guid.CreateVersion7(),
            SourceKey = sourceKey,
            ExternalRef = externalRef,
            Title = title,
            SourceText = text,
            ContentHash = text is null ? null : HashOf(text),
            AttachmentId = attachmentId,
            Status = KnowledgeDocumentStatus.Pending,
            Revision = 1,
            CreatedAt = now
        };
    }

    /// <summary>
    /// New content for the same document. Returns false (title updated, nothing to index) when the content is the one
    /// already being indexed, or already indexed with <paramref name="embeddingModel"/>. A failed document, and one
    /// indexed with another embedding model, is indexed again.
    /// </summary>
    public bool Replace(string title, string? text, Guid? attachmentId, string embeddingModel)
    {
        if ((text is null) == (attachmentId is null))
            throw new ArgumentException("A knowledge document has either text or an attachment.");

        Title = title;
        var hash = text is null ? null : HashOf(text);
        var same = hash == ContentHash && attachmentId == AttachmentId;
        var current = Status == KnowledgeDocumentStatus.Pending ||
                      (Status == KnowledgeDocumentStatus.Ready && EmbeddingModel == embeddingModel);
        if (same && current)
            return false;

        SourceText = text;
        ContentHash = hash;
        AttachmentId = attachmentId;
        Status = KnowledgeDocumentStatus.Pending;
        Revision++;
        Attempts = 0;
        FailureCode = null;
        return true;
    }

    public void MarkEnqueued(DateTime now) => LastEnqueuedAt = now;

    /// <summary>
    /// An indexing job starts working on <paramref name="revision"/>; false when there is nothing to do. Counts the
    /// attempt and marks the document as queued now, so the stall sweep leaves a working job alone.
    /// </summary>
    public bool BeginAttempt(int revision, DateTime now)
    {
        if (!IsPendingRevision(revision))
            return false;
        Attempts++;
        LastEnqueuedAt = now;
        return true;
    }

    /// <summary>The revision is current and still waits for its passages.</summary>
    public bool IsPendingRevision(int revision) => revision == Revision && Status == KnowledgeDocumentStatus.Pending;

    public void MarkIndexed(int revision, int chunkCount, string embeddingModel, DateTime now)
    {
        EnsureCurrent(revision);
        Status = KnowledgeDocumentStatus.Ready;
        IndexedRevision = revision;
        ChunkCount = chunkCount;
        EmbeddingModel = embeddingModel;
        FailureCode = null;
        IndexedAt = now;
    }

    /// <summary>The revision cannot be indexed. Its passages are removed by the caller in the same save.</summary>
    public void MarkFailed(int revision, string code)
    {
        EnsureCurrent(revision);
        Status = KnowledgeDocumentStatus.Failed;
        IndexedRevision = null;
        ChunkCount = 0;
        FailureCode = code.Length <= FailureCodeMaxLength ? code : code[..FailureCodeMaxLength];
    }

    /// <summary>Waiting for a job longer than <paramref name="quietPeriod"/> (a lost enqueue or a dead worker).</summary>
    public bool IsStalled(DateTime now, TimeSpan quietPeriod) =>
        Status == KnowledgeDocumentStatus.Pending && (LastEnqueuedAt ?? CreatedAt) < now - quietPeriod;

    private void EnsureCurrent(int revision)
    {
        if (revision != Revision)
            throw new InvalidOperationException($"Knowledge document {Id}: revision {revision} is no longer current ({Revision}).");
    }

    public static string HashOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>
/// One searchable passage of a document revision. Besides the mapped columns the table has two columns managed in SQL
/// (migration <c>AddKnowledgeAndMemory</c>): <c>embedding vector</c> (pgvector) and <c>search_vector tsvector</c>
/// (generated from <see cref="Content"/>). They are written and read only by the indexing job and the search query,
/// so the EF model needs no provider plugin for them.
/// </summary>
public class KnowledgeChunk : Entity<Guid>, IMustHaveTenant
{
    public Guid TenantId { get; set; }

    public Guid DocumentId { get; private set; }

    public int Revision { get; private set; }

    /// <summary>Position in the document (0-based); part of the citation.</summary>
    public int Ordinal { get; private set; }

    public string Content { get; private set; } = default!;

    /// <summary>Character offset of the passage in the extracted text.</summary>
    public int StartOffset { get; private set; }

    private KnowledgeChunk()
    {
    }

    public static KnowledgeChunk Create(Guid documentId, int revision, int ordinal, string content, int startOffset) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            Revision = revision,
            Ordinal = ordinal,
            Content = content,
            StartOffset = startOffset
        };
}
