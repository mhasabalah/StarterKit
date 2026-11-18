using Agentic.Contracts.Knowledge;
using Agentic.Domains.Knowledge.Models;
using Agentic.Runtime.Knowledge;
using Attachment.Contracts.Attachments.Queries;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Shared.Core.MultiTenancy.BackgroundJobs;

namespace Agentic.Domains.Knowledge.Jobs;

/// <summary>
/// Indexes ONE revision of a document: read (text or attachment), extract, split into passages, embed, and replace the
/// document's passages in one transaction. The document row decides everything (Hangfire's retry is off):
/// <list type="bullet">
/// <item>A job for a revision that is no longer current, or already indexed, does nothing; a duplicate is harmless.</item>
/// <item>The result is accepted only if the revision is still current when it is saved (optimistic concurrency), so
/// a slow job never overwrites newer content.</item>
/// <item>Content that can never be indexed (unsupported, too large, no text) fails at once; a provider failure is
/// retried with backoff up to <c>Agentic:Knowledge:MaxAttempts</c>, then the document is <c>Failed</c>.</item>
/// </list>
/// It runs on the <c>agentic</c> queue in the document's tenant (restored by Hangfire) and acts for nobody: the
/// content was chosen by the owning module, and the attachment is read with the trusted contract query.
/// </summary>
[Queue(AgenticOptions.Queue)]
[AutomaticRetry(Attempts = 0)]
public sealed class IndexKnowledgeDocumentJob(
    IUnitOfWorkRunner unitOfWork,
    IKnowledgeEmbedder embedder,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    ILogger<IndexKnowledgeDocumentJob> logger)
{
    /// <param name="Exhausted">The revision used up its attempts (e.g. a worker died on it): fail it, do not retry.</param>
    private sealed record Work(Guid Id, Guid TenantId, int Revision, int Attempts, string? Text, Guid? AttachmentId,
        bool Exhausted = false);

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task ExecuteAsync(Guid documentId, int revision, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.Knowledge.Enabled)
            return;

        var work = await BeginAsync(documentId, revision, settings.Knowledge.MaxAttempts);
        if (work is null)
            return;
        if (work.Exhausted)
        {
            logger.LogError("Knowledge document {DocumentId} revision {Revision} used up its {Attempts} attempts", work.Id,
                work.Revision, work.Attempts);
            await FailAsync(work, "indexing_failed");
            return;
        }

        IReadOnlyList<TextChunk> chunks;
        IReadOnlyList<float[]> vectors;
        try
        {
            var knowledge = settings.Knowledge;
            var text = work.Text is not null
                ? KnowledgeTextExtractor.Normalize(work.Text, knowledge.MaxTextLength)
                : await ReadAttachmentAsync(work.AttachmentId!.Value, knowledge, cancellationToken);
            chunks = KnowledgeChunker.Split(text, knowledge.ChunkSize, knowledge.ChunkOverlap);
            vectors = await embedder.EmbedAsync(chunks.Select(c => c.Content).ToList(), cancellationToken);
        }
        catch (KnowledgeIndexingException ex)
        {
            logger.LogWarning("Knowledge document {DocumentId} revision {Revision} cannot be indexed: {Code}", work.Id,
                work.Revision, ex.Code);
            await FailAsync(work, ex.Code);
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // shutdown: the document stays Pending and the stall sweep queues it again
        }
        catch (Exception ex)
        {
            if (work.Attempts >= settings.Knowledge.MaxAttempts)
            {
                logger.LogError(ex, "Knowledge document {DocumentId} revision {Revision} failed {Attempts} times", work.Id,
                    work.Revision, work.Attempts);
                await FailAsync(work, "indexing_failed");
            }
            else
            {
                var delay = Backoff(work.Attempts);
                logger.LogWarning(ex, "Knowledge document {DocumentId} revision {Revision}: attempt {Attempt} failed, retry in {Delay}",
                    work.Id, work.Revision, work.Attempts, delay);
                await RescheduleAsync(work, delay);
            }

            return;
        }

        await StoreAsync(work, chunks, vectors);
    }

    /// <summary>15 s, 1 min, 4 min, ... capped at 15 min.</summary>
    public static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(15 * Math.Pow(4, Math.Max(0, attempt - 1)), 900));

    private async Task<Work?> BeginAsync(Guid documentId, int revision, int maxAttempts)
    {
        try
        {
            return await unitOfWork.RunAsync(async (services, ct) =>
            {
                var db = services.GetRequiredService<AgenticDbContext>();
                var document = await db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct);
                if (document is null || !document.IsPendingRevision(revision))
                    return null;

                // Attempts that never reached the job's own error handling (the process died) count too: a document
                // that keeps killing workers is failed instead of being queued again forever.
                if (document.Attempts >= maxAttempts)
                {
                    return new Work(document.Id, document.TenantId, document.Revision, document.Attempts, null, null,
                        Exhausted: true);
                }

                document.BeginAttempt(revision, Now);
                await db.SaveChangesAsync(ct);
                return new Work(document.Id, document.TenantId, document.Revision, document.Attempts, document.SourceText,
                    document.AttachmentId);
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict())
        {
            return null; // replaced at this moment: the new revision has its own job
        }
    }

    private Task<string> ReadAttachmentAsync(Guid attachmentId, KnowledgeOptions knowledge, CancellationToken cancellationToken) =>
        unitOfWork.RunAsync(async (services, ct) =>
        {
            OpenAttachmentContentResult content;
            try
            {
                content = await services.GetRequiredService<ISender>().Send(new OpenAttachmentContentQuery(attachmentId), ct);
            }
            catch (NotFoundException)
            {
                throw new KnowledgeIndexingException("attachment_not_found");
            }

            await using var stream = content.Content;
            return await KnowledgeTextExtractor.ExtractAsync(stream, content.FileName, content.ContentType, knowledge, ct);
        }, cancellationToken);

    private async Task StoreAsync(Work work, IReadOnlyList<TextChunk> chunks, IReadOnlyList<float[]> vectors)
    {
        try
        {
            var stored = await unitOfWork.RunAsync(async (services, ct) =>
            {
                var db = services.GetRequiredService<AgenticDbContext>();
                var document = await db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == work.Id, ct);
                if (document is null || document.Revision != work.Revision || document.Status != KnowledgeDocumentStatus.Pending)
                    return false;

                // Old and new passages swap in one transaction: until it commits, searches keep using the previously
                // indexed revision (IndexedRevision); afterwards only the new one. Never a mix.
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await db.KnowledgeChunks.Where(c => c.DocumentId == document.Id).ExecuteDeleteAsync(ct);
                var rows = chunks.Select((c, i) => KnowledgeChunk.Create(document.Id, work.Revision, i, c.Content, c.StartOffset))
                    .ToList();
                db.KnowledgeChunks.AddRange(rows);
                document.MarkIndexed(work.Revision, rows.Count, embedder.ModelIdentity, Now);
                await db.SaveChangesAsync(ct);
                await KnowledgeStore.SetEmbeddingsAsync(db, work.TenantId, rows.Select(r => r.Id).ToList(), vectors, ct);
                await transaction.CommitAsync(ct);
                return true;
            }, CancellationToken.None);

            if (stored)
            {
                logger.LogInformation("Knowledge document {DocumentId} revision {Revision} indexed: {Chunks} passage(s)", work.Id,
                    work.Revision, chunks.Count);
            }
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict())
        {
            logger.LogInformation("Knowledge document {DocumentId} changed while revision {Revision} was indexed; result dropped",
                work.Id, work.Revision);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // A duplicate job stored the same revision first.
            logger.LogInformation("Knowledge document {DocumentId} revision {Revision} was stored by another job", work.Id,
                work.Revision);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            // Removed by its module (or with its attachment) while the passages were written: nothing to keep.
            logger.LogInformation("Knowledge document {DocumentId} was removed while revision {Revision} was indexed", work.Id,
                work.Revision);
        }
    }

    private async Task FailAsync(Work work, string code)
    {
        try
        {
            await unitOfWork.RunAsync(async (services, ct) =>
            {
                var db = services.GetRequiredService<AgenticDbContext>();
                var document = await db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == work.Id, ct);
                if (document is null || document.Revision != work.Revision || document.Status != KnowledgeDocumentStatus.Pending)
                    return;

                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await db.KnowledgeChunks.Where(c => c.DocumentId == document.Id).ExecuteDeleteAsync(ct);
                document.MarkFailed(work.Revision, code);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict())
        {
            // Replaced meanwhile: the new revision has its own job.
        }
    }

    private async Task RescheduleAsync(Work work, TimeSpan delay)
    {
        try
        {
            await unitOfWork.RunAsync(async (services, ct) =>
            {
                var db = services.GetRequiredService<AgenticDbContext>();
                var document = await db.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == work.Id, ct);
                if (document is null || document.Revision != work.Revision)
                    return;
                document.MarkEnqueued(Now + delay);
                await db.SaveChangesAsync(ct);
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict())
        {
            return;
        }

        KnowledgeQueue.Schedule(jobs, work.Id, work.Revision, delay);
    }
}

/// <summary>
/// Safety net (recurring, per tenant): queues again every pending document that was not queued recently (a crash
/// between saving the document and queueing its job, a worker that stopped). Duplicate jobs are harmless.
/// </summary>
[DisableConcurrentExecution(60)]
public sealed class RequeueStalledKnowledgeDocumentsJob(
    IUnitOfWorkRunner unitOfWork,
    ITenantJobRunner tenantJobs,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    IOptions<AgenticOptions> options,
    ILogger<RequeueStalledKnowledgeDocumentsJob> logger)
{
    internal const int BatchSize = 100;

    public async Task<int> ExecuteAsync(IJobCancellationToken? cancellationToken)
    {
        if (!options.Value.Enabled || !options.Value.Knowledge.Enabled)
            return 0;

        var total = 0;
        await tenantJobs.ForEachTenantScopeAsync(async (_, _) => total += await RequeueCurrentTenantAsync(cancellationToken),
            nameof(RequeueStalledKnowledgeDocumentsJob));
        return total;
    }

    public async Task<int> RequeueCurrentTenantAsync(IJobCancellationToken? cancellationToken)
    {
        cancellationToken?.ThrowIfCancellationRequested();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var quiet = TimeSpan.FromMinutes(options.Value.Knowledge.StalledAfterMinutes);
        var cutoff = now - quiet;

        List<(Guid Id, int Revision)> stalled;
        try
        {
            stalled = await unitOfWork.RunAsync(async (services, ct) =>
        {
            var db = services.GetRequiredService<AgenticDbContext>();
            var due = await db.KnowledgeDocuments
                .Where(d => d.Status == KnowledgeDocumentStatus.Pending)
                .Where(d => (d.LastEnqueuedAt ?? d.CreatedAt) < cutoff)
                .OrderBy(d => d.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(ct);
            foreach (var document in due)
                document.MarkEnqueued(now);
            await db.SaveChangesAsync(ct);
            return due.Select(d => (d.Id, d.Revision)).ToList();
        }, CancellationToken.None);
        }
        catch (Exception ex) when (ex.IsConcurrencyConflict())
        {
            // A job started one of them at this moment; the next sweep looks again.
            logger.LogInformation("RequeueStalledKnowledgeDocumentsJob: a document changed meanwhile, skipped this round");
            return 0;
        }

        foreach (var (id, revision) in stalled)
        {
            KnowledgeQueue.Enqueue(jobs, id, revision);
            logger.LogWarning("Knowledge document {DocumentId} was stalled and has been queued again", id);
        }

        return stalled.Count;
    }
}
