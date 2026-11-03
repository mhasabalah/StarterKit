using Shared.Core.MultiTenancy;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Domains.Attachments.Models;
using Attachment.Infrastructure.BackgroundJobs;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Maintenance;
using Attachment.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Core.Data;

namespace Attachment.Tests.Integration;

/// <summary>The reconciliation sweeper against PostgreSQL and real Local storage.</summary>
[Collection(AttachmentHostCollection.Name)]
public sealed class ReconciliationTests : IDisposable
{
    private readonly AttachmentHostFixture _host;

    public ReconciliationTests(AttachmentHostFixture host)
    {
        _host = host;
        _host.Reset();
    }

    public void Dispose() => _host.Reset();

    /// <summary>A sweeper with its own options (the host's are the defaults: scan disabled).</summary>
    private AttachmentReconciler Reconciler(IServiceProvider sp, Action<ReconciliationOptions> configure)
    {
        var options = TestOptions.Create();
        configure(options.Reconciliation);
        return new AttachmentReconciler(
            sp.GetRequiredService<IUnitOfWorkRunner>(), _host.Storage, Options.Create(options), _host.Clock,
            new DefaultTenantStore(), TenantContext.SingleTenant, NullLogger<AttachmentReconciler>.Instance);
    }

    private async Task<string> WriteBlob(string path, TimeSpan age)
    {
        await _host.Storage.Inner.UploadToPathAsync(new MemoryStream(TestFiles.Pdf()), path, "application/pdf");
        File.SetLastWriteTimeUtc(Path.Combine([_host.StorageRoot, .. path.Split('/')]), DateTime.UtcNow - age);
        return path;
    }

    private async Task<AttachmentFile> SeedReady(string path)
    {
        var attachment = AttachmentFile.Create(Guid.NewGuid(), "f.pdf", "f.pdf", "application/pdf", 10, path, "/u",
            AttachmentType.Document, Guid.NewGuid());
        await _host.DbAsync(async db =>
        {
            db.Attachments.Add(attachment);
            return await db.SaveChangesAsync();
        });
        return attachment;
    }

    [Fact]
    public async Task Storage_scan_respects_the_safety_window_dry_run_and_row_references()
    {
        var run = Guid.NewGuid().ToString("N");
        var oldOrphan = await WriteBlob($"scan/{run}/old-orphan.pdf", TimeSpan.FromHours(30));
        var freshOrphan = await WriteBlob($"scan/{run}/fresh-orphan.pdf", TimeSpan.FromHours(1));
        var referenced = await WriteBlob($"scan/{run}/referenced.pdf", TimeSpan.FromHours(30));
        await SeedReady(referenced);

        // Dry run: reported, nothing deleted.
        var dry = await _host.InScopeAsync(sp => Reconciler(sp, o =>
        {
            o.StorageScan.Enabled = true;
            o.StorageScan.DryRun = true;
            o.StorageScan.SafetyWindowHours = 24;
            o.BatchSize = 2; // several DB lookups
        }).ScanStorageAsync(_host.Clock.GetUtcNow().UtcDateTime));

        dry.Orphans.Should().BeGreaterThanOrEqualTo(1);
        dry.Deleted.Should().Be(0);
        _host.FileExists(oldOrphan).Should().BeTrue("dry run deletes nothing");

        // Real run.
        var real = await _host.InScopeAsync(sp => Reconciler(sp, o =>
        {
            o.StorageScan.Enabled = true;
            o.StorageScan.DryRun = false;
            o.StorageScan.SafetyWindowHours = 24;
        }).ScanStorageAsync(_host.Clock.GetUtcNow().UtcDateTime));

        real.Deleted.Should().BeGreaterThanOrEqualTo(1);
        _host.FileExists(oldOrphan).Should().BeFalse("an old blob without a row is an orphan");
        _host.FileExists(freshOrphan).Should().BeTrue("blobs inside the safety window are never touched");
        _host.FileExists(referenced).Should().BeTrue("a blob referenced by a row is never touched");
    }

    [Fact]
    public async Task Storage_scan_keeps_blobs_of_pending_rows_and_is_off_by_default()
    {
        var run = Guid.NewGuid().ToString("N");
        var pendingBlob = await WriteBlob($"temp/{run}/p.pdf", TimeSpan.FromDays(3));
        var orphan = await WriteBlob($"temp/{run}/orphan.pdf", TimeSpan.FromDays(3));
        var pending = PendingAttachment.Create(Guid.NewGuid(), Guid.NewGuid(), "p.pdf", "p.pdf", "application/pdf", 10,
            pendingBlob, "pending", AttachmentType.Document, AttachmentCategory.General, 0, false, 48);
        await _host.DbAsync(async db =>
        {
            db.PendingAttachments.Add(pending);
            return await db.SaveChangesAsync();
        });

        var byDefault = await _host.InScopeAsync(sp =>
            sp.GetRequiredService<AttachmentReconciler>().ScanStorageAsync(_host.Clock.GetUtcNow().UtcDateTime));
        byDefault.Should().Be((0, 0, 0), "the scan is disabled unless configured");
        _host.FileExists(orphan).Should().BeTrue();

        await _host.InScopeAsync(sp => Reconciler(sp, o =>
        {
            o.StorageScan.Enabled = true;
            o.StorageScan.DryRun = false;
        }).ScanStorageAsync(_host.Clock.GetUtcNow().UtcDateTime));

        _host.FileExists(pendingBlob).Should().BeTrue("pending rows reference blobs too");
        _host.FileExists(orphan).Should().BeFalse();
    }

    [Fact]
    public async Task Purge_removes_old_deleted_and_associated_pending_rows_only()
    {
        var session = Guid.NewGuid();
        PendingAttachment Make(int order) => PendingAttachment.Create(Guid.NewGuid(), session, "p.pdf", "p.pdf",
            "application/pdf", 10, $"temp/{session:N}/{order}.pdf", "pending", AttachmentType.Document,
            AttachmentCategory.General, order, false, 48);

        var deleted = Make(0);
        deleted.MarkAsDeleted();
        var associated = Make(1);
        associated.MarkAsAssociated();
        var live = Make(2);

        await _host.DbAsync(async db =>
        {
            db.PendingAttachments.AddRange(deleted, associated, live);
            return await db.SaveChangesAsync();
        });

        var tooEarly = await _host.InScopeAsync(sp => sp.GetRequiredService<AttachmentReconciler>()
            .PurgePendingRowsAsync(_host.Clock.GetUtcNow().UtcDateTime));

        _host.Clock.Advance(TimeSpan.FromDays(31));
        await _host.InScopeAsync(sp => sp.GetRequiredService<AttachmentReconciler>()
            .PurgePendingRowsAsync(_host.Clock.GetUtcNow().UtcDateTime));

        var remaining = await _host.DbAsync(db => db.PendingAttachments.IgnoreQueryFilters()
            .Where(p => p.SessionId == session).Select(p => p.Id).ToListAsync());

        tooEarly.Should().Be(0);
        remaining.Should().Equal(live.Id);
    }

    [Fact]
    public async Task Recurring_job_runs_every_phase_and_reports()
    {
        // An abandoned upload and a due deletion, both handled by one run.
        var abandoned = AttachmentFile.StartUpload(Guid.NewGuid(), "a.pdf", "a.pdf", "application/pdf", 10,
            await WriteBlob($"job/{Guid.NewGuid():N}.pdf", TimeSpan.Zero), "/u", AttachmentType.Document, Guid.NewGuid());
        var deleting = AttachmentFile.Create(Guid.NewGuid(), "d.pdf", "d.pdf", "application/pdf", 10,
            await WriteBlob($"job/{Guid.NewGuid():N}.pdf", TimeSpan.Zero), "/u", AttachmentType.Document, Guid.NewGuid());
        deleting.RequestDeletion(_host.Clock.GetUtcNow().UtcDateTime);

        await _host.DbAsync(async db =>
        {
            db.Attachments.AddRange(abandoned, deleting);
            return await db.SaveChangesAsync();
        });

        _host.Clock.Advance(TimeSpan.FromHours(2));
        await _host.InScopeAsync(sp => sp.GetRequiredService<AttachmentReconciliationJob>().ExecuteAsync(CancellationToken.None));

        var left = await _host.DbAsync(db => db.Attachments.IgnoreQueryFilters()
            .Where(a => a.Id == abandoned.Id || a.Id == deleting.Id).CountAsync());
        left.Should().Be(0);
        _host.FileExists(abandoned.StoragePath).Should().BeFalse();
        _host.FileExists(deleting.StoragePath).Should().BeFalse();
    }
}
