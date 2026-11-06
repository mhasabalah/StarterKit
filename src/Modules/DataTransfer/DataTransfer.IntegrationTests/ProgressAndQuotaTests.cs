using Shared.Core.Entitlements;
using A = DataTransfer.IntegrationTests.DataTransferPostgresFixture;

namespace DataTransfer.IntegrationTests;

/// <summary>Live progress (hub events) and the plan row quota, on the real module and database.</summary>
[Collection(DataTransferPostgresCollection.Name)]
public sealed class ProgressAndQuotaTests(DataTransferPostgresFixture fx) : IAsyncLifetime
{
    private const string FiveRows = "code,name,qty\nA,Apple,1\nB,Banana,2\nC,Cherry,3\nD,Date,4\nE,Elder,5\n";
    private const int Running = 2;
    private const int Completed = 3;
    private const int Failed = 5;

    public Task InitializeAsync() => fx.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Progress_is_pushed_to_the_requester_after_each_batch_and_ends_with_the_final_counters()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);
        fx.Items.AfterBatch = () =>
        {
            fx.Clock.Advance(TimeSpan.FromSeconds(2)); // past the throttle: every checkpoint is pushed
            return Task.CompletedTask;
        };

        await fx.RunImportAsync(A.TenantA, job.Id);

        var events = fx.Events.For(job.Id);
        events.Should().OnlyContain(e => e.Type == "datatransfer.job-progress" && e.TenantId == A.TenantA.Id);
        fx.Events.All.Should().OnlyContain(e => e.User == fx.Caller.UserId, "only the requester's sessions");
        events.Select(e => Convert.ToInt32(e.Data["status"])).Should().StartWith(Running).And.EndWith(Completed);
        events.Select(e => Convert.ToInt64(e.Data["processedRows"])).Should().BeInAscendingOrder()
            .And.Contain([0L, 2L, 4L, 5L], "the claim, three checkpoints and the final state");
        Convert.ToInt64(events[^1].Data["createdRows"]).Should().Be(5);
    }

    [Fact]
    public async Task Progress_is_throttled_but_the_final_state_is_always_sent()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows); // the clock does not move: one interval

        await fx.RunImportAsync(A.TenantA, job.Id);

        var statuses = fx.Events.For(job.Id).Select(e => Convert.ToInt32(e.Data["status"])).ToList();
        statuses.Should().Equal(Running, Completed);
    }

    [Fact]
    public async Task Cancelling_a_pending_job_pushes_its_final_state()
    {
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);

        await fx.SendAsync(A.TenantA, new CancelTransferJobCommand(job.Id));

        fx.Events.For(job.Id).Should().ContainSingle().Which.Data["status"].Should().Be((int)TransferJobStatus.Cancelled);
    }

    [Fact]
    public async Task A_commit_stops_with_quota_exceeded_when_the_plan_runs_out_and_keeps_what_was_written()
    {
        fx.Quota.Limit = 3; // batch 1 (2 rows) fits, batch 2 (2 more) does not
        var job = await fx.StartImportAsync(A.TenantA, FiveRows);

        await fx.RunImportAsync(A.TenantA, job.Id);

        var done = await fx.JobAsync(A.TenantA, job.Id);
        done.Should().BeEquivalentTo(new { Status = TransferJobStatus.Failed, FailureCode = "quota_exceeded", CreatedRows = 2L });
        done.FailureMessage.Should().Contain("2 of 3");
        fx.Items.Count(A.TenantA.Id).Should().Be(2);
        fx.Quota.Keys.Should().OnlyContain(k => k == EntitlementKeys.DataTransferRows);
        fx.Jobs.For(job.Id).Should().NotContain(j => j.IsScheduled, "a used-up quota is not retried");
        fx.Events.For(job.Id)[^1].Data["failureCode"].Should().Be("quota_exceeded");
    }

    [Fact]
    public async Task A_dry_run_and_a_validate_first_pass_consume_no_quota()
    {
        await RunAsync(await fx.StartImportAsync(A.TenantA, FiveRows, ImportMode.DryRun));
        fx.Quota.Used.Should().Be(0);

        await RunAsync(await fx.StartImportAsync(A.TenantA, FiveRows, policy: ImportPolicy.ValidateFirst));
        fx.Quota.Used.Should().Be(5, "only the commit pass counts");
    }

    [Fact]
    public async Task Exports_consume_the_same_row_quota()
    {
        foreach (var code in new[] { "A", "B", "C" })
            fx.Items.Rows[(A.TenantA.Id, code)] = (code, 1);

        var ok = await fx.SendAsync(A.TenantA, new StartExportCommand(TestItemImportDefinition.DefinitionKey, TransferFormat.Csv));
        await fx.RunExportAsync(A.TenantA, ok.Id);
        fx.Quota.Used.Should().Be(3);

        fx.Quota.Limit = 4;
        var denied = await fx.SendAsync(A.TenantA, new StartExportCommand(TestItemImportDefinition.DefinitionKey, TransferFormat.Csv));
        await fx.RunExportAsync(A.TenantA, denied.Id);

        (await fx.JobAsync(A.TenantA, denied.Id)).Should().BeEquivalentTo(new
        {
            Status = TransferJobStatus.Failed,
            FailureCode = "quota_exceeded",
            ResultAttachmentId = (Guid?)null
        });
    }

    private async Task RunAsync(TransferJobDto job)
    {
        await fx.RunImportAsync(A.TenantA, job.Id);
        (await fx.JobAsync(A.TenantA, job.Id)).Status.Should().BeOneOf(TransferJobStatus.Completed, TransferJobStatus.CompletedWithErrors);
    }
}
