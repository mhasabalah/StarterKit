using Agentic.Domains.Knowledge.Jobs;
using Hangfire;

namespace Agentic.Domains.Knowledge;

/// <summary>A source key nobody registered (400 <c>knowledge_source_not_found</c>).</summary>
public sealed class KnowledgeSourceNotFoundException : BadRequestException
{
    public KnowledgeSourceNotFoundException(string key)
        : base(AgenticText.Get("Agent.Knowledge.source_not_found", key)) => ErrorCode = "knowledge_source_not_found";
}

/// <summary>Knowledge is switched off or has no embedding model (<c>knowledge_unavailable</c>).</summary>
public sealed class KnowledgeUnavailableException : BadRequestException
{
    public KnowledgeUnavailableException()
        : base(AgenticText.Get("Agent.Knowledge.unavailable")) => ErrorCode = "knowledge_unavailable";
}

/// <summary>Queues the indexing job of a document revision (the <c>agentic</c> queue comes from the job attributes).</summary>
public static class KnowledgeQueue
{
    public static string Enqueue(IBackgroundJobClient jobs, Guid documentId, int revision) =>
        jobs.Enqueue<IndexKnowledgeDocumentJob>(job => job.ExecuteAsync(documentId, revision, CancellationToken.None));

    public static string Schedule(IBackgroundJobClient jobs, Guid documentId, int revision, TimeSpan delay) =>
        jobs.Schedule<IndexKnowledgeDocumentJob>(job => job.ExecuteAsync(documentId, revision, CancellationToken.None), delay);
}
