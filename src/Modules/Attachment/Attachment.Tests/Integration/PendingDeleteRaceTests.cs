using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Data;
using Attachment.Data.Repository.Attachments;
using Attachment.Domains.Attachments.Features.DeletePendingAttachment;
using Attachment.Domains.Attachments.Models;
using Attachment.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Attachment.Tests.Integration;

/// <summary>
/// Deleting a pending image while its thumbnail job saves the row, on real PostgreSQL: the job's commit (thumbnail
/// path + version) lands between the delete's read and write, a REAL optimistic-concurrency conflict. The delete must
/// still succeed and remove the new thumbnail file too. It used to answer 409 and leave a row whose original file was
/// already deleted.
/// </summary>
[Collection(AttachmentPostgresCollection.Name)]
public sealed class PendingDeleteRaceTests(AttachmentPostgresFixture db) : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task Delete_wins_against_a_thumbnail_saved_mid_delete_and_leaves_no_files()
    {
        var caller = Caller.User(Guid.NewGuid());
        var sessions = TestServices.Sessions(caller);
        var storage = TestServices.LocalStorage(_dir.Path);
        var sessionId = sessions.IssueForCurrentCaller();
        var id = Guid.NewGuid();
        var original = $"temp/{sessionId:N}/{id:N}.png";
        var thumbnail = $"temp/{sessionId:N}/{id:N}_thumb.jpg";

        await storage.UploadToPathAsync(new MemoryStream(TestFiles.Png()), original, "image/png");
        await using (var seed = db.CreateContext(caller))
        {
            seed.PendingAttachments.Add(PendingAttachment.Create(id, sessionId, $"{id:N}.png", "me.png", "image/png", 10,
                original, "pending", AttachmentType.Image, AttachmentCategory.General, 0, false, 1));
            await seed.SaveChangesAsync();
        }

        // What GenerateThumbnailJob does, committed from another context just before the delete saves.
        async Task ThumbnailJobCommitsAsync()
        {
            await storage.UploadToPathAsync(new MemoryStream([1, 2, 3]), thumbnail, "image/jpeg");
            await using var job = db.CreateContext(caller);
            var row = await job.PendingAttachments.SingleAsync(p => p.Id == id);
            row.SetThumbnail(thumbnail, "pending-thumb", 20, 20);
            await job.SaveChangesAsync();
        }

        await using var context = db.CreateContext(caller);
        var repository = new RacingRepository(new PendingAttachmentRepository(context), ThumbnailJobCommitsAsync);
        var handler = new DeletePendingAttachmentHandler(repository, storage, sessions,
            NullLogger<DeletePendingAttachmentHandler>.Instance);

        (await handler.Handle(new DeletePendingAttachmentCommand(id, sessionId), CancellationToken.None)).Should().BeTrue();

        repository.Conflicted.Should().BeTrue("the race really happened");
        await using var check = db.CreateContext(caller);
        (await check.PendingAttachments.IgnoreQueryFilters().SingleAsync(p => p.Id == id)).IsDeleted.Should().BeTrue();
        _dir.AllFiles().Should().BeEmpty("the original AND the thumbnail written mid-delete are removed");
    }

    /// <summary>The real repository, with the thumbnail job's commit injected before the FIRST save.</summary>
    private sealed class RacingRepository(IPendingAttachmentRepository inner, Func<Task> beforeFirstSave)
        : IPendingAttachmentRepository
    {
        private bool _raced;

        public bool Conflicted { get; private set; }

        public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_raced)
            {
                _raced = true;
                await beforeFirstSave();
            }

            try
            {
                await inner.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                Conflicted = true;
                throw;
            }
        }

        public Task<PendingAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.GetByIdAsync(id, cancellationToken);

        public Task<List<PendingAttachment>> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            inner.GetBySessionIdAsync(sessionId, cancellationToken);

        public Task<int> GetCountBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            inner.GetCountBySessionIdAsync(sessionId, cancellationToken);

        public Task<bool> DisplayOrderExistsAsync(Guid sessionId, int displayOrder, CancellationToken cancellationToken = default) =>
            inner.DisplayOrderExistsAsync(sessionId, displayOrder, cancellationToken);

        public Task<List<PendingAttachment>> GetExpiredUnassociatedAsync(DateTime cutoffDate, int batchSize,
            IReadOnlyCollection<Guid> excludeIds, CancellationToken cancellationToken = default) =>
            inner.GetExpiredUnassociatedAsync(cutoffDate, batchSize, excludeIds, cancellationToken);

        public Task<IDbContextTransaction> BeginSessionLockAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            inner.BeginSessionLockAsync(sessionId, cancellationToken);

        public Task AddAsync(PendingAttachment pendingAttachment, CancellationToken cancellationToken = default) =>
            inner.AddAsync(pendingAttachment, cancellationToken);

        public Task<bool> ReloadAsync(PendingAttachment pendingAttachment, CancellationToken cancellationToken = default) =>
            inner.ReloadAsync(pendingAttachment, cancellationToken);
    }
}
