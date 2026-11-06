using System.Text;
using DataTransfer.Tests.Engine;
using DataTransfer.Domains.TransferJobs.EventHandlers;
using DataTransfer.Domains.TransferJobs.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.MultiTenancy.Authorization;
using A = DataTransfer.IntegrationTests.DataTransferPostgresFixture;

namespace DataTransfer.IntegrationTests;

/// <summary>Import jobs end to end on real Postgres (BatchSize = 2, MaxAttempts = 3).</summary>
[Collection(DataTransferPostgresCollection.Name)]
public sealed class ImportJobTests(DataTransferPostgresFixture fx) : IAsyncLifetime
{
    private const string FiveRows = "code,name,qty\nA,Apple,1\nB,Banana,2\nC,Cherry,3\nD,Date,4\nE,Elder,5\n";

    public Task InitializeAsync() => fx.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task DryRun_validates_every_row_writes_nothing_and_reports_the_invalid_rows()
    {
        var job = await fx.StartImportAsync(A.TenantA, "code,name,qty\nA,Apple,1\nB,Banana,lots\nC,Cherry,3\n",
            ImportMode.DryRun);

        await fx.RunImportAsync(A.TenantA, job.Id);

        var done = await fx.JobAsync(A.TenantA, job.Id);
        done.Should().BeEquivalentTo(new
        {
            Status = TransferJobStatus.CompletedWithErrors,
            TotalRows = (long?)3,
            ProcessedRows = 3L,
            CreatedRows = 2L,
            FailedRows = 1L,
            StoredErrors = 1
        });
        fx.Items.Count(A.TenantA.Id).Should().Be(0, "a dry run writes nothing");

        var detail = await fx.SendAsync(A.TenantA, new GetTransferJobByIdQuery(job.Id));
        detail.FirstErrors.Should().ContainSingle().Which.Should().Be(
            new TransferJobErrorDto(3, "qty", "invalid_number", "'lots' is not a number."));

        var report = Encoding.UTF8.GetString(fx.Attachments.Files[done.ErrorReportAttachmentId!.Value].Content);
        report.Should().Contain("Row,Column,Code,Message").And.Contain("3,qty,invalid_number");
        fx.Notifications.InApp.Should().Contain(n => n.User == fx.Caller.UserId &&
                                                     n.Content.Category == TransferJobFinishedEventHandler.Category);
    }

    [Fact]
    public async Task Commit_writes_in_batches_checkpoints_and_puts_the_integration_event_in_the_outbox()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);

        await fx.RunImportAsync(A.TenantA, job.Id);

        var done = await fx.JobAsync(A.TenantA, job.Id);
        done.Status.Should().Be(TransferJobStatus.Completed);
        done.CreatedRows.Should().Be(5);
        done.LastCommittedRow.Should().Be(6, "the last data row is file row 6");
        done.ErrorReportAttachmentId.Should().BeNull();
        fx.Items.Batches.Should().Be(3, "5 rows in batches of 2");
        fx.Items.Count(A.TenantA.Id).Should().Be(5);

        var outbox = await fx.AdminScalarAsync<long>(
            $"SELECT count(*) FROM datatransfer.outbox_messages WHERE tenant_id = '{A.TenantA.Id}' AND payload::text LIKE '%{job.Id}%'");
        outbox.Should().Be(1);
    }

    [Fact]
    public async Task Reimporting_the_same_file_changes_nothing()
    {
        await RunToEndAsync(await fx.StartImportAsync(A.TenantA, FiveRows));

        var second = await RunToEndAsync(await fx.StartImportAsync(A.TenantA, FiveRows));

        second.Should().BeEquivalentTo(new { Status = TransferJobStatus.Completed, UnchangedRows = 5L, CreatedRows = 0L });
        fx.Items.Writes.Values.Should().AllBeEquivalentTo(1);
    }

    [Fact]
    public async Task ValidateFirst_with_one_invalid_row_writes_nothing_and_fails_with_validation_failed()
    {
        var job = await fx.StartImportAsync(A.TenantA, "code,name,qty\nA,Apple,1\nB,Banana,x\nC,Cherry,3\n",
            policy: ImportPolicy.ValidateFirst);

        var done = await RunToEndAsync(job);

        done.Status.Should().Be(TransferJobStatus.Failed);
        done.FailureCode.Should().Be("validation_failed");
        done.ErrorReportAttachmentId.Should().NotBeNull();
        fx.Items.Count(A.TenantA.Id).Should().Be(0);
    }

    [Fact]
    public async Task ValidateFirst_with_all_rows_valid_validates_then_commits_once()
    {
        var done = await RunToEndAsync(await fx.StartImportAsync(A.TenantA, FiveRows, policy: ImportPolicy.ValidateFirst));

        done.Should().BeEquivalentTo(new { Status = TransferJobStatus.Completed, Validated = true, CreatedRows = 5L, ProcessedRows = 5L });
        fx.Items.Count(A.TenantA.Id).Should().Be(5);
        fx.Items.Batches.Should().Be(6, "3 validation batches + 3 commit batches");
    }

    [Fact]
    public async Task A_crash_mid_run_is_retried_from_the_checkpoint_without_writing_a_row_twice()
    {
        fx.Items.ThrowOnCode = "C"; // second batch (C, D) fails once
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);

        await fx.RunImportAsync(A.TenantA, job.Id);

        var afterCrash = await fx.JobAsync(A.TenantA, job.Id);
        afterCrash.Should().BeEquivalentTo(new { Status = TransferJobStatus.Running, LastCommittedRow = 3L, LeaseOwner = (string?)null });
        fx.Jobs.For(job.Id).Should().Contain(j => j.IsScheduled, "the next attempt is scheduled with backoff");
        fx.Items.Count(A.TenantA.Id).Should().Be(2);

        await fx.RunImportAsync(A.TenantA, job.Id);

        var done = await fx.JobAsync(A.TenantA, job.Id);
        done.Should().BeEquivalentTo(new
        {
            Status = TransferJobStatus.Completed,
            Attempts = 2,
            CreatedRows = 5L,
            UnchangedRows = 0L,
            ProcessedRows = 5L
        }, "the retry resumes after the checkpoint instead of re-reading the first batch");
        fx.Items.Batches.Should().Be(4, "A,B + failed C,D + C,D + E: the committed batch is not applied again");
        fx.Items.Writes.Values.Should().AllBeEquivalentTo(1);
    }

    [Fact]
    public async Task A_job_that_fails_every_attempt_ends_failed_with_internal_error()
    {
        fx.Items.ThrowOnCode = "A";
        fx.Items.AlwaysThrow = true;
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);

        for (var i = 0; i < 3; i++)
            await fx.RunImportAsync(A.TenantA, job.Id);

        var done = await fx.JobAsync(A.TenantA, job.Id);
        done.Should().BeEquivalentTo(new { Status = TransferJobStatus.Failed, FailureCode = "internal_error", Attempts = 3 });
        fx.Jobs.For(job.Id).Count(j => j.IsScheduled).Should().Be(2);
    }

    [Fact]
    public async Task A_file_missing_a_required_column_fails_at_once_without_retry()
    {
        var job = await fx.StartImportAsync(A.TenantA, "code,qty\nA,1\n");

        var done = await RunToEndAsync(job);

        done.Should().BeEquivalentTo(new { Status = TransferJobStatus.Failed, FailureCode = "missing_columns", Attempts = 1 });
        fx.Items.Batches.Should().Be(0);
        fx.Jobs.For(job.Id).Should().NotContain(j => j.IsScheduled);
    }

    [Fact]
    public async Task A_second_import_of_the_same_definition_while_one_is_active_is_409_and_stores_no_file()
    {
        await fx.StartImportAsync(A.TenantA, FiveRows);
        var files = fx.Attachments.Files.Count;

        var act = () => fx.StartImportAsync(A.TenantA, FiveRows);

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be("import_already_running");
        fx.Attachments.Files.Count.Should().Be(files);
    }

    [Fact]
    public async Task Another_tenant_may_import_the_same_definition_at_the_same_time()
    {
        await fx.StartImportAsync(A.TenantA, FiveRows);

        var other = await fx.StartImportAsync(A.TenantB, FiveRows);

        other.Status.Should().Be(TransferJobStatus.Pending);
    }

    [Fact]
    public async Task Cancelling_a_pending_job_finishes_it_and_the_worker_then_does_nothing()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);

        var cancelled = await fx.SendAsync(A.TenantA, new CancelTransferJobCommand(job.Id));
        await fx.RunImportAsync(A.TenantA, job.Id);

        cancelled.Status.Should().Be(TransferJobStatus.Cancelled);
        fx.Items.Batches.Should().Be(0);
    }

    [Fact]
    public async Task Cancelling_a_running_job_stops_it_between_batches_and_keeps_committed_rows()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);
        fx.Items.AfterBatch = async () =>
        {
            fx.Items.AfterBatch = null;
            await fx.SendAsync(A.TenantA, new CancelTransferJobCommand(job.Id));
        };

        await fx.RunImportAsync(A.TenantA, job.Id);

        var done = await fx.JobAsync(A.TenantA, job.Id);
        done.Status.Should().Be(TransferJobStatus.Cancelled);
        fx.Items.Count(A.TenantA.Id).Should().Be(2, "the first batch was committed before the cancel was seen");
        fx.Items.Batches.Should().Be(1);
    }

    [Fact]
    public async Task Cancel_is_idempotent_while_running_and_a_finished_job_cannot_be_cancelled()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);
        fx.Items.AfterBatch = async () =>
        {
            fx.Items.AfterBatch = null;
            await fx.SendAsync(A.TenantA, new CancelTransferJobCommand(job.Id)); // the worker bumped the version meanwhile
            await fx.SendAsync(A.TenantA, new CancelTransferJobCommand(job.Id)); // repeated: harmless
        };
        await fx.RunImportAsync(A.TenantA, job.Id);

        var act = () => fx.SendAsync(A.TenantA, new CancelTransferJobCommand(job.Id));

        (await act.Should().ThrowAsync<BadRequestException>()).Which.ErrorCode.Should().Be("invalid_job_state");
        (await fx.JobAsync(A.TenantA, job.Id)).Status.Should().Be(TransferJobStatus.Cancelled);
    }

    [Fact]
    public async Task A_requester_who_left_the_tenant_stops_the_job_before_anything_is_written()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);
        fx.Caller.NotMembers.Add(fx.Caller.UserId);

        await fx.RunImportAsync(A.TenantA, job.Id);

        (await fx.JobAsync(A.TenantA, job.Id)).FailureCode.Should().Be("requester_no_longer_authorized");
        fx.Items.Batches.Should().Be(0);
    }

    [Fact]
    public async Task A_requester_who_lost_the_definition_permission_stops_the_job_on_the_retry()
    {
        fx.Items.ThrowOnCode = "C";
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);
        await fx.RunImportAsync(A.TenantA, job.Id); // first batch committed, then a transient failure

        fx.Caller.Denied.Add(TenantPermissions.ProductsManage);
        await fx.RunImportAsync(A.TenantA, job.Id);

        var done = await fx.JobAsync(A.TenantA, job.Id);
        done.Should().BeEquivalentTo(new { Status = TransferJobStatus.Failed, FailureCode = "requester_no_longer_authorized" });
        fx.Items.Count(A.TenantA.Id).Should().Be(2, "nothing after the revocation");
    }

    [Fact]
    public async Task The_definition_runs_in_the_requesters_branch_scope()
    {
        var branch = Guid.NewGuid();
        fx.Caller.AllowedBranches = [branch];

        await RunToEndAsync(await fx.StartImportAsync(A.TenantA, FiveRows));

        fx.Items.ObservedBranches!.AllowedBranchIds.Should().Equal(branch);
    }

    [Fact]
    public async Task Failure_messages_and_row_errors_are_in_the_requesters_language()
    {
        var job = await fx.StartImportAsync(A.TenantA, "code,qty\nA,1\n", culture: "ar");

        var done = await RunToEndAsync(job);

        done.Culture.Should().Be("ar");
        done.FailureMessage.Should().StartWith("أعمدة مطلوبة غير موجودة: الاسم");
    }

    [Fact]
    public async Task An_excel_authored_workbook_imports_through_the_job()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dt-it-{Guid.NewGuid():N}.xlsx");
        ExcelAuthoredWorkbook.Create(path,
        [
            [new ExcelAuthoredWorkbook.Text("Code"), new ExcelAuthoredWorkbook.Text("Name"), new ExcelAuthoredWorkbook.Text("Quantity")],
            [new ExcelAuthoredWorkbook.Number(1234567890123456), new ExcelAuthoredWorkbook.Text("Big code"), new ExcelAuthoredWorkbook.Number(3)],
            [new ExcelAuthoredWorkbook.Text("B"), new ExcelAuthoredWorkbook.Text("Banana"), new ExcelAuthoredWorkbook.Number(7)]
        ]);
        var bytes = await File.ReadAllBytesAsync(path);
        File.Delete(path);

        var done = await RunToEndAsync(await fx.StartImportAsync(A.TenantA, bytes, "items.xlsx"));

        done.Should().BeEquivalentTo(new { Status = TransferJobStatus.Completed, CreatedRows = 2L, TotalRows = (long?)2 });
        fx.Items.Rows[(A.TenantA.Id, "1234567890123456")].Should().Be(("Big code", 3));
    }

    [Fact]
    public async Task A_run_that_loses_its_lease_mid_run_stops_without_touching_the_job()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);
        fx.Items.AfterBatch = async () =>
        {
            fx.Items.AfterBatch = null;
            fx.Clock.Advance(TimeSpan.FromMinutes(6)); // the lease (5 min) expired: another worker takes over
            await fx.InTenantAsync(A.TenantA, async sp =>
            {
                var repository = sp.GetRequiredService<DataTransfer.Data.Repository.ITransferJobRepository>();
                var row = await repository.GetByIdAsync(job.Id);
                row!.TryClaim("other-worker", fx.Clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(5)).Should().BeTrue();
                await repository.SaveChangesAsync();
                return true;
            });
        };

        await fx.RunImportAsync(A.TenantA, job.Id);

        var row = await fx.JobAsync(A.TenantA, job.Id);
        row.Should().BeEquivalentTo(new { Status = TransferJobStatus.Running, LeaseOwner = "other-worker", ProcessedRows = 0L });
        fx.Items.Batches.Should().Be(1, "the old run stopped instead of applying the next batch");
        fx.Jobs.For(job.Id).Should().NotContain(j => j.IsScheduled, "a lost lease is not a failure to retry");
    }

    [Fact]
    public async Task A_shutdown_mid_run_releases_the_lease_so_the_rerun_resumes_at_once()
    {
        using var shutdown = new CancellationTokenSource();
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);
        fx.Items.AfterBatch = () =>
        {
            fx.Items.AfterBatch = null;
            shutdown.Cancel();
            return Task.CompletedTask;
        };

        var act = () => fx.RunImportAsync(A.TenantA, job.Id, shutdown.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var row = await fx.JobAsync(A.TenantA, job.Id);
        row.Should().BeEquivalentTo(new { Status = TransferJobStatus.Running, LeaseOwner = (string?)null });

        await fx.RunImportAsync(A.TenantA, job.Id);
        (await fx.JobAsync(A.TenantA, job.Id)).Should().BeEquivalentTo(new { Status = TransferJobStatus.Completed, CreatedRows = 5L });
    }

    [Fact]
    public async Task Row_errors_beyond_the_storage_cap_are_counted_but_not_stored()
    {
        fx.Options.MaxStoredErrors = 2;
        var job = await fx.StartImportAsync(A.TenantA, "code,name,qty\nA,a,x\nB,b,x\nC,c,x\nD,d,x\n", ImportMode.DryRun);

        var done = await RunToEndAsync(job);

        done.Should().BeEquivalentTo(new { FailedRows = 4L, StoredErrors = 2 });
        var report = Encoding.UTF8.GetString(fx.Attachments.Files[done.ErrorReportAttachmentId!.Value].Content);
        report.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(3, "header + 2 stored errors");
    }

    [Fact]
    public async Task The_purge_works_through_more_than_one_batch_in_a_run()
    {
        const int jobs = PurgeExpiredTransferJobsJob.BatchSize + 30;
        await fx.InTenantDbAsync(A.TenantA, async db =>
        {
            for (var i = 0; i < jobs; i++)
            {
                var job = TransferJob.CreateExport(TestItemImportDefinition.DefinitionKey, TransferFormat.Csv, null,
                    fx.Caller.UserId, "en");
                job.RequestCancel(fx.Clock.GetUtcNow().UtcDateTime.AddDays(-30), 7);
                db.Jobs.Add(job);
            }

            return await db.SaveChangesAsync();
        });

        var purged = await fx.InTenantAsync(A.TenantA, sp =>
            sp.GetRequiredService<PurgeExpiredTransferJobsJob>().PurgeCurrentTenantAsync(null));

        purged.Should().BeGreaterThanOrEqualTo(jobs);
        (await fx.InTenantDbAsync(A.TenantA, db => db.Jobs.CountAsync(j => j.ExpiresAt < DateTime.UtcNow)))
            .Should().Be(0);
    }

    [Fact]
    public async Task While_another_worker_holds_a_live_lease_a_duplicate_run_does_nothing()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);
        await fx.InTenantAsync(A.TenantA, async sp =>
        {
            var repository = sp.GetRequiredService<DataTransfer.Data.Repository.ITransferJobRepository>();
            var row = await repository.GetByIdAsync(job.Id);
            row!.TryClaim("other-worker", fx.Clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(5));
            await repository.SaveChangesAsync();
            return true;
        });

        await fx.RunImportAsync(A.TenantA, job.Id);

        var row = await fx.JobAsync(A.TenantA, job.Id);
        row.LeaseOwner.Should().Be("other-worker");
        row.ProcessedRows.Should().Be(0);
        fx.Items.Batches.Should().Be(0);
    }

    [Fact]
    public async Task Commit_of_a_finished_dry_run_reuses_its_file()
    {
        var dryRun = await RunToEndAsync(await fx.StartImportAsync(A.TenantA, FiveRows, ImportMode.DryRun));

        var commit = await fx.SendAsync(A.TenantA, new CommitImportCommand(dryRun.Id));
        var done = await RunToEndAsync(commit);

        done.SourceAttachmentId.Should().Be(dryRun.SourceAttachmentId);
        done.DryRunJobId.Should().Be(dryRun.Id);
        done.CreatedRows.Should().Be(5);
        fx.Items.Count(A.TenantA.Id).Should().Be(5);
    }

    [Fact]
    public async Task Without_the_definition_permission_the_import_is_forbidden_and_nothing_is_stored()
    {
        fx.Caller.Denied.Add(TenantPermissions.ProductsManage);
        var files = fx.Attachments.Files.Count;

        var act = () => fx.StartImportAsync(A.TenantA, FiveRows);

        await act.Should().ThrowAsync<ForbiddenException>();
        fx.Attachments.Files.Count.Should().Be(files);
    }

    [Fact]
    public async Task An_unknown_definition_is_404_and_an_unsupported_file_type_is_400()
    {
        var unknown = () => fx.StartImportAsync(A.TenantA, FiveRows, definition: "nope.things");
        await unknown.Should().ThrowAsync<NotFoundException>();

        await using var stream = new MemoryStream([1, 2, 3]);
        var pdf = () => fx.SendAsync(A.TenantA, new StartImportCommand(TestItemImportDefinition.DefinitionKey, "items.pdf",
            "application/pdf", 3, stream));
        (await pdf.Should().ThrowAsync<BadRequestException>()).Which.ErrorCode.Should().Be("unsupported_format");
    }

    [Fact]
    public async Task Jobs_are_private_to_their_requester_and_invisible_to_other_tenants()
    {
        fx.Caller.Denied.Add(TenantPermissions.AuditRead); // the colleague is a plain member, not an auditor
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);
        var colleague = Guid.NewGuid();

        var otherTenant = () => fx.SendAsync(A.TenantB, new GetTransferJobByIdQuery(job.Id));
        var otherUser = () => fx.SendAsync(A.TenantA, new GetTransferJobByIdQuery(job.Id), colleague);
        var otherUserCancel = () => fx.SendAsync(A.TenantA, new CancelTransferJobCommand(job.Id), colleague);

        await otherTenant.Should().ThrowAsync<NotFoundException>();
        await otherUser.Should().ThrowAsync<NotFoundException>();
        await otherUserCancel.Should().ThrowAsync<NotFoundException>();
        (await fx.SendAsync(A.TenantB, new GetTransferJobsQuery())).Result.Should().NotContain(j => j.Id == job.Id);
        (await fx.SendAsync(A.TenantA, new GetTransferJobsQuery(), colleague)).Result.Should().NotContain(j => j.Id == job.Id);
    }

    [Fact]
    public async Task An_auditor_reads_every_job_of_the_tenant()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);
        var auditor = Guid.NewGuid();

        var detail = await fx.SendAsync(A.TenantA, new GetTransferJobByIdQuery(job.Id), auditor);

        detail.Job.Id.Should().Be(job.Id);
    }

    [Fact]
    public async Task A_member_without_audit_read_sees_only_their_own_jobs()
    {
        fx.Caller.Denied.Add(TenantPermissions.AuditRead);
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);

        var mine = await fx.SendAsync(A.TenantA, new GetTransferJobsQuery { Kind = TransferJobKind.Import });
        var theirs = await fx.SendAsync(A.TenantA, new GetTransferJobsQuery(), Guid.NewGuid());

        mine.Result.Should().Contain(j => j.Id == job.Id);
        theirs.Result.Should().NotContain(j => j.Id == job.Id);
    }

    [Fact]
    public async Task Rls_hides_another_tenants_rows_even_without_ef_filters()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);

        var seenFromB = await fx.InTenantDbAsync(A.TenantB, db =>
            db.Jobs.IgnoreQueryFilters().AsNoTracking().CountAsync(j => j.Id == job.Id));

        seenFromB.Should().Be(0);
    }

    [Fact]
    public async Task The_stall_sweep_queues_a_lost_job_again_but_not_a_recently_queued_one()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);
        var sweep = () => fx.InTenantAsync(A.TenantA, sp =>
            sp.GetRequiredService<RequeueStalledTransferJobsJob>().RequeueCurrentTenantAsync(null));

        (await sweep()).Should().Be(0, "it was queued just now");

        fx.Clock.Advance(TimeSpan.FromMinutes(11));
        var before = fx.Jobs.For(job.Id).Count;
        (await sweep()).Should().BeGreaterThanOrEqualTo(1);
        fx.Jobs.For(job.Id).Count.Should().Be(before + 1);
        (await sweep()).Should().Be(0, "the sweep marked it as queued");
    }

    [Fact]
    public async Task Expired_jobs_are_purged_with_their_files_but_a_dry_run_used_by_a_live_commit_is_kept()
    {
        var dryRun = await RunToEndAsync(await fx.StartImportAsync(A.TenantA, "code,name,qty\nA,Apple,x\n", ImportMode.DryRun));
        var commit = await fx.SendAsync(A.TenantA, new CommitImportCommand(dryRun.Id));
        var finishedCommit = await RunToEndAsync(commit);
        fx.Attachments.Of(dryRun.Id).Should().NotBeEmpty();
        var purge = () => fx.InTenantAsync(A.TenantA, sp =>
            sp.GetRequiredService<PurgeExpiredTransferJobsJob>().PurgeCurrentTenantAsync(null));

        // Commit expires one tick after the dry run: in between, the dry run must stay (its file is the commit's source).
        await fx.AdminExecAsync($"UPDATE datatransfer.transfer_jobs SET expires_at = now() - interval '1 day' WHERE id = '{dryRun.Id}'");
        await fx.AdminExecAsync($"UPDATE datatransfer.transfer_jobs SET expires_at = now() + interval '1 day' WHERE id = '{finishedCommit.Id}'");
        await purge();
        (await fx.InTenantDbAsync(A.TenantA, db => db.Jobs.AnyAsync(j => j.Id == dryRun.Id))).Should().BeTrue();

        await fx.AdminExecAsync($"UPDATE datatransfer.transfer_jobs SET expires_at = now() - interval '1 day' WHERE id = '{finishedCommit.Id}'");
        await purge();

        (await fx.InTenantDbAsync(A.TenantA, db => db.Jobs.AnyAsync(j => j.Id == dryRun.Id || j.Id == finishedCommit.Id)))
            .Should().BeFalse();
        (await fx.InTenantDbAsync(A.TenantA, db => db.Errors.AnyAsync(e => e.JobId == dryRun.Id))).Should().BeFalse();
        fx.Attachments.Of(dryRun.Id).Should().BeEmpty();
    }

    private async Task<TransferJob> RunToEndAsync(TransferJobDto job)
    {
        await fx.RunImportAsync(A.TenantA, job.Id);
        return await fx.JobAsync(A.TenantA, job.Id);
    }
}
