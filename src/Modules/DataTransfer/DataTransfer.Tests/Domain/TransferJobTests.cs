using DataTransfer.Contracts.IntegrationEvents;
using DataTransfer.Domains.TransferJobs.Events;

namespace DataTransfer.Tests.Domain;

public sealed class TransferJobTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly Guid User = Guid.NewGuid();

    private static TransferJob Import(ImportMode mode = ImportMode.Commit) =>
        TransferJob.CreateImport(Guid.NewGuid(), "test.items", TransferFormat.Csv, mode, ImportPolicy.BestEffort,
            "items.csv", Guid.NewGuid(), User, "ar-EG");

    [Fact]
    public void New_import_is_pending_with_normalized_culture() =>
        Import().Should().BeEquivalentTo(new { Status = TransferJobStatus.Pending, Culture = "ar", Attempts = 0 });

    [Fact]
    public void Claim_takes_the_lease_and_a_second_worker_is_refused_while_it_is_live()
    {
        var job = Import();

        job.TryClaim("w1", Now, Lease).Should().BeTrue();
        job.TryClaim("w2", Now.AddMinutes(1), Lease).Should().BeFalse();

        job.Status.Should().Be(TransferJobStatus.Running);
        job.HoldsLease("w1").Should().BeTrue();
        job.Attempts.Should().Be(1);
    }

    [Fact]
    public void An_expired_lease_can_be_taken_over_and_the_old_owner_loses_it()
    {
        var job = Import();
        job.TryClaim("w1", Now, Lease);

        job.TryClaim("w2", Now + Lease + TimeSpan.FromSeconds(1), Lease).Should().BeTrue();

        job.HoldsLease("w1").Should().BeFalse();
        job.HoldsLease("w2").Should().BeTrue();
        job.Attempts.Should().Be(2);
    }

    [Fact]
    public void A_finished_job_cannot_be_claimed()
    {
        var job = Import();
        job.TryClaim("w1", Now, Lease);
        job.Complete(Now, 7);

        job.TryClaim("w2", Now, Lease).Should().BeFalse();
    }

    [Fact]
    public void Cancelling_a_pending_job_finishes_it_at_once()
    {
        var job = Import();

        job.RequestCancel(Now, 7);

        job.Status.Should().Be(TransferJobStatus.Cancelled);
        job.ExpiresAt.Should().Be(Now.AddDays(7));
        job.DomainEvents.OfType<TransferJobFinishedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Cancelling_a_running_job_only_flags_it_until_the_worker_confirms()
    {
        var job = Import();
        job.TryClaim("w1", Now, Lease);

        job.RequestCancel(Now, 7);
        job.Status.Should().Be(TransferJobStatus.Running);
        job.CancelRequested.Should().BeTrue();

        job.ConfirmCancelled(Now, 7);
        job.Status.Should().Be(TransferJobStatus.Cancelled);
        job.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public void Cancelling_a_finished_job_is_rejected()
    {
        var job = Import();
        job.RequestCancel(Now, 7);

        var act = () => job.RequestCancel(Now, 7);

        act.Should().Throw<InvalidTransferJobStateException>();
    }

    [Fact]
    public void Complete_with_failed_rows_is_completed_with_errors()
    {
        var job = Import();
        job.TryClaim("w1", Now, Lease);
        job.RecordBatch(new BatchCounts(3, 1, 0, 2), lastRow: 7, storedErrors: 2);

        job.Complete(Now, 7);

        job.Should().BeEquivalentTo(new
        {
            Status = TransferJobStatus.CompletedWithErrors,
            ProcessedRows = 6L,
            CreatedRows = 3L,
            FailedRows = 2L,
            LastCommittedRow = 7L,
            StoredErrors = 2
        });
    }

    [Fact]
    public void Only_a_commit_import_raises_the_integration_event()
    {
        var commit = Import(ImportMode.Commit);
        var dryRun = Import(ImportMode.DryRun);
        foreach (var job in new[] { commit, dryRun })
        {
            job.TryClaim("w", Now, Lease);
            job.Complete(Now, 7);
        }

        commit.IntegrationEvents.OfType<DataImportCompletedIntegrationEvent>().Should().ContainSingle()
            .Which.JobId.Should().Be(commit.Id);
        dryRun.IntegrationEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_failed_job_raises_no_integration_event_and_keeps_the_failure()
    {
        var job = Import();
        job.TryClaim("w", Now, Lease);

        job.Fail("validation_failed", "2 rows are invalid", Now, 7);

        job.Status.Should().Be(TransferJobStatus.Failed);
        job.FailureCode.Should().Be("validation_failed");
        job.IntegrationEvents.Should().BeEmpty();
    }

    [Fact]
    public void Finishing_twice_is_rejected()
    {
        var job = Import();
        job.TryClaim("w", Now, Lease);
        job.Complete(Now, 7);

        var act = () => job.Fail("x", "y", Now, 7);

        act.Should().Throw<InvalidTransferJobStateException>();
    }

    [Fact]
    public void MarkValidated_restarts_the_counters_and_checkpoint_for_the_commit_pass()
    {
        var job = Import();
        job.RecordBatch(new BatchCounts(0, 0, 5, 0), lastRow: 6, storedErrors: 0);

        job.MarkValidated();

        job.Should().BeEquivalentTo(new { Validated = true, ProcessedRows = 0L, UnchangedRows = 0L, LastCommittedRow = 0L });
    }

    [Fact]
    public void Commit_from_a_completed_dry_run_reuses_its_source_file()
    {
        var dryRun = Import(ImportMode.DryRun);
        dryRun.TryClaim("w", Now, Lease);
        dryRun.Complete(Now, 7);

        var commit = TransferJob.CreateCommitFrom(dryRun, ImportPolicy.ValidateFirst, User, "en");

        commit.Should().BeEquivalentTo(new
        {
            Mode = (ImportMode?)ImportMode.Commit,
            Policy = (ImportPolicy?)ImportPolicy.ValidateFirst,
            dryRun.SourceAttachmentId,
            DryRunJobId = (Guid?)dryRun.Id,
            Status = TransferJobStatus.Pending
        });
    }

    [Fact]
    public void Commit_from_an_unfinished_dry_run_or_a_commit_is_rejected()
    {
        var running = Import(ImportMode.DryRun);
        var commit = Import(ImportMode.Commit);
        commit.TryClaim("w", Now, Lease);
        commit.Complete(Now, 7);

        ((Action)(() => TransferJob.CreateCommitFrom(running, null, User, "en"))).Should().Throw<InvalidTransferJobStateException>();
        ((Action)(() => TransferJob.CreateCommitFrom(commit, null, User, "en"))).Should().Throw<InvalidTransferJobStateException>();
    }

    [Fact]
    public void IsStalled_needs_no_live_lease_and_no_recent_enqueue()
    {
        var quiet = TimeSpan.FromMinutes(10);
        var job = Import();
        job.MarkEnqueued(Now);

        job.IsStalled(Now.AddMinutes(5), quiet).Should().BeFalse("it was queued recently");
        job.IsStalled(Now.AddMinutes(11), quiet).Should().BeTrue("the enqueue was lost");

        job.TryClaim("w", Now.AddMinutes(11), Lease);
        job.IsStalled(Now.AddMinutes(12), quiet).Should().BeFalse("a worker holds a live lease");
        job.IsStalled(Now.AddMinutes(30), quiet).Should().BeTrue("the worker crashed: its lease expired");
    }

    [Fact]
    public void Export_with_an_oversized_filter_is_rejected()
    {
        var act = () => TransferJob.CreateExport("test.items", TransferFormat.Xlsx, new string('x', 5000), User, "en");

        act.Should().Throw<InvalidTransferJobStateException>();
    }
}
