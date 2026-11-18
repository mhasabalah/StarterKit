using Agentic.Contracts.Approvals;
using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Domains.AgentRuns.Exceptions;
using Agentic.Domains.AgentRuns.Jobs;
using Agentic.Domains.Approvals.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.MultiTenancy;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy.Authorization;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>
/// Human approval: a run that proposes a sensitive action is suspended (no worker held), a person decides on the exact
/// payload, and the run resumes. An approval can be used once, only for what was approved, and replaces no other check.
/// </summary>
[Collection(AgenticPostgresCollection.Name)]
public class AgentApprovalTests
{
    private const string MailArguments = """{"title":"Invoice 1045"}""";

    private readonly AgenticPostgresFixture _fx;

    public AgentApprovalTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
    }

    /// <summary>A run that asked to send a mail and is now waiting for a person.</summary>
    private async Task<Guid> SuspendedRunAsync(Guid? user = null)
    {
        _fx.Board.Play(
            ScriptedReply.Call(SendMailTool.Name, MailArguments),
            ScriptedReply.Say("The mail was handled."));
        var started = await _fx.StartAsync(TenantA, user: user);
        await _fx.RunAsync(TenantA, started.Id);
        return started.Id;
    }

    [Fact]
    public async Task ASensitiveAction_SuspendsTheRun_WithARequestBoundToTheExactPayload_AndHoldsNoWorker()
    {
        var runId = await SuspendedRunAsync();

        var run = await _fx.RunOfAsync(TenantA, runId);
        run.Status.Should().Be(AgentRunStatus.AwaitingApproval);
        run.LeaseOwner.Should().BeNull();
        run.LeaseUntil.Should().BeNull();
        run.ToolCalls.Should().Be(0);

        var approval = await _fx.ApprovalOfAsync(TenantA, runId);
        approval.Status.Should().Be(AgentApprovalStatus.Pending);
        approval.ToolName.Should().Be(SendMailTool.Name);
        approval.ArgumentsJson.Should().Be(MailArguments);
        approval.PayloadDigest.Should().HaveLength(64);
        approval.RequiredPermission.Should().Be(TenantPermissions.WebhooksManage);
        approval.RequestedBy.Should().Be(TestCaller.Alice);
        approval.ExpiresAt.Should().BeAfter(approval.RequestedAt.AddHours(71));
        _fx.World.Mail.Should().BeEmpty();
    }

    [Fact]
    public async Task ASuspendedRun_IsLeftAlone_ByDuplicateJobsAndByTheStallSweep()
    {
        var runId = await SuspendedRunAsync();
        var turns = _fx.Board.Turns;
        var jobs = _fx.Jobs.For(runId).Count;

        await _fx.RunAsync(TenantA, runId);
        _fx.Clock.Advance(TimeSpan.FromHours(2));
        await _fx.SweepAsync(TenantA);

        (await _fx.RunOfAsync(TenantA, runId)).Status.Should().Be(AgentRunStatus.AwaitingApproval);
        _fx.Board.Turns.Should().Be(turns);
        _fx.Jobs.For(runId).Should().HaveCount(jobs, "a run that waits for a person is not stalled");
        _fx.World.Mail.Should().BeEmpty();
    }

    private Task<RequeueStalledAgentRunsJob.Backlog> BacklogOfAsync(TenantInfo tenant) =>
        _fx.InTenantAsync(tenant, sp => sp.GetRequiredService<RequeueStalledAgentRunsJob>().MeasureCurrentTenantAsync());

    [Fact]
    public async Task TheBacklogGauges_CountAWaitingRequest_InItsTenantOnly_AndReportTheOldest()
    {
        var beforeA = await BacklogOfAsync(TenantA);
        var beforeB = await BacklogOfAsync(TenantB);

        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);

        var afterA = await BacklogOfAsync(TenantA);
        afterA.PendingApprovals.Should().Be(beforeA.PendingApprovals + 1);
        afterA.OldestApproval.Should().NotBeNull();
        afterA.OldestApproval!.Value.Should().BeOnOrBefore(approval.RequestedAt);
        (await BacklogOfAsync(TenantB)).PendingApprovals.Should().Be(beforeB.PendingApprovals);
    }

    [Fact]
    public async Task TheQueuedAge_KeepsGrowing_WhileNoWorkerTakesTheRun_AlthoughTheSweepQueuesItAgain()
    {
        var started = await _fx.StartAsync(TenantA); // queued, and no worker ever runs it
        var created = (await _fx.RunOfAsync(TenantA, started.Id)).CreatedAt!.Value;

        _fx.Clock.Advance(TimeSpan.FromMinutes(30));
        await _fx.SweepAsync(TenantA);
        _fx.Clock.Advance(TimeSpan.FromMinutes(30));
        await _fx.SweepAsync(TenantA);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.LastEnqueuedAt.Should().BeAfter(created, "the sweep queued the stalled run again");
        var backlog = await BacklogOfAsync(TenantA);
        backlog.OldestQueued.Should().NotBeNull();
        backlog.OldestQueued!.Value.Should().BeOnOrBefore(created, "re-enqueueing must not reset the age the alert reads");
    }

    [Fact]
    public async Task Approve_QueuesTheRun_WhichExecutesTheActionExactlyOnce_AndUsesTheApprovalUp()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);

        var decided = await _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest, "Looks right"),
            TestCaller.Bob);

        decided.Status.Should().Be(AgentApprovalStatus.Approved);
        decided.DecidedBy.Should().Be(TestCaller.Bob);
        (await _fx.RunOfAsync(TenantA, runId)).Status.Should().Be(AgentRunStatus.Queued);
        _fx.World.Mail.Should().BeEmpty("approving only queues the run");

        await _fx.RunAsync(TenantA, runId);

        var run = await _fx.RunOfAsync(TenantA, runId);
        run.Status.Should().Be(AgentRunStatus.Completed);
        run.OutputText.Should().Be("The mail was handled.");
        _fx.World.Mail.Should().Equal("Invoice 1045");
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Consumed);
        var call = (await _fx.StepsOfAsync(TenantA, runId)).Single(s => s.Kind == AgentRunStepKind.ToolCall);
        call.Status.Should().Be(AgentRunStepStatus.Succeeded);
        call.Executions.Should().Be(1);

        // The mail is sent as the initiator (Alice), not as the approver.
        _fx.World.Observations.Single(o => o.Tool == SendMailTool.Name).UserId.Should().Be(TestCaller.Alice);
    }

    [Fact]
    public async Task TheDecision_IsWrittenToTheAuditTrail_InTheSameTransaction()
    {
        var runId = await SuspendedRunAsync();
        await _fx.ApproveAndRunAsync(TenantA, runId, TestCaller.Bob);
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);

        var entries = await _fx.AdminScalarAsync<long>(
            $"select count(*) from agentic.audit_entries where entity_id = '{approval.Id}' and tenant_id = '{TenantA.Id}'");
        var byBob = await _fx.AdminScalarAsync<long>(
            $"select count(*) from agentic.audit_entries where entity_id = '{approval.Id}' and actor_user_id = '{TestCaller.Bob}'");

        entries.Should().BeGreaterThanOrEqualTo(2, "the request and the decision are both recorded");
        byBob.Should().BeGreaterThan(0);

        // The audit trail identifies the payload by its digest and does not copy the payload itself.
        var withDigest = await _fx.AdminScalarAsync<long>(
            $"select count(*) from agentic.audit_entries where entity_id = '{approval.Id}' and changes_json::text like '%{approval.PayloadDigest}%'");
        var withContent = await _fx.AdminScalarAsync<long>(
            $"select count(*) from agentic.audit_entries where entity_id = '{approval.Id}' and changes_json::text like '%Invoice 1045%'");
        withDigest.Should().BeGreaterThan(0);
        withContent.Should().Be(0);
    }

    [Fact]
    public async Task Reject_ResumesTheRun_TellsTheAgentWhy_AndTheActionNeverHappens()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);

        await _fx.SendAsync(TenantA, new RejectAgentActionCommand(approval.Id, "Wrong customer"), TestCaller.Bob);
        await _fx.RunAsync(TenantA, runId);

        var run = await _fx.RunOfAsync(TenantA, runId);
        run.Status.Should().Be(AgentRunStatus.Completed);
        var call = (await _fx.StepsOfAsync(TenantA, runId)).Single(s => s.Kind == AgentRunStepKind.ToolCall);
        call.Status.Should().Be(AgentRunStepStatus.Rejected);
        call.ErrorCode.Should().Be("rejected");
        call.ResultJson.Should().Contain("Wrong customer");
        call.Executions.Should().Be(0);
        _fx.World.Mail.Should().BeEmpty();
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Rejected);
    }

    [Fact]
    public async Task Approve_WithAHashOtherThanThePayloadOnRecord_IsAConflict_AndNothingChanges()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);

        var act = () => _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, new string('0', 64)));

        (await act.Should().ThrowAsync<AgentApprovalConflictException>()).Which.ErrorCode.Should().Be("approval_payload_mismatch");
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Pending);
        (await _fx.RunOfAsync(TenantA, runId)).Status.Should().Be(AgentRunStatus.AwaitingApproval);
    }

    [Fact]
    public async Task APayload_ChangedAfterItWasApproved_IsNotExecuted_AndIsProposedAgain()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);
        await _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest), TestCaller.Bob);
        // Someone with database access rewrites the arguments between the approval and the execution.
        await _fx.AdminExecAsync(
            "update agentic.agent_run_steps set arguments_json = '{\"title\":\"Pay the attacker\"}' " +
            $"where run_id = '{runId}' and kind = 'ToolCall'");

        await _fx.RunAsync(TenantA, runId);

        _fx.World.Mail.Should().BeEmpty("consent was given for another payload");
        (await _fx.RunOfAsync(TenantA, runId)).Status.Should().Be(AgentRunStatus.AwaitingApproval);
        var again = await _fx.ApprovalOfAsync(TenantA, runId);
        again.Status.Should().Be(AgentApprovalStatus.Pending);
        again.DecidedBy.Should().BeNull();
        again.PayloadDigest.Should().Be(approval.PayloadDigest, "the request still describes the action that was reviewed");
    }

    [Fact]
    public async Task AnApprover_WithoutTheToolsOwnPermission_CannotApprove()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);
        _fx.Caller.DeniedTo.Add((TestCaller.Bob, TenantPermissions.WebhooksManage));

        var act = () => _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest), TestCaller.Bob);

        (await act.Should().ThrowAsync<AgentApprovalDeniedException>()).Which.ErrorCode.Should().Be("approver_lacks_permission");
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Pending);
    }

    [Fact]
    public async Task AUser_WithoutTheDecidePermission_CanNeitherApproveNorReject()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);
        _fx.Caller.DeniedTo.Add((TestCaller.Bob, TenantPermissions.AgentApprovalsDecide));

        var approve = () => _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest), TestCaller.Bob);
        var reject = () => _fx.SendAsync(TenantA, new RejectAgentActionCommand(approval.Id), TestCaller.Bob);

        (await approve.Should().ThrowAsync<AgentApprovalDeniedException>()).Which.ErrorCode.Should().Be("decide_permission_required");
        await reject.Should().ThrowAsync<AgentApprovalDeniedException>();
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Pending);
    }

    [Fact]
    public async Task WithTheFourEyesRule_TheInitiatorCannotApproveTheirOwnRun_ButSomeoneElseCan()
    {
        _fx.Options.Approvals.RequireDifferentApprover = true;
        var runId = await SuspendedRunAsync(TestCaller.Alice);
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);

        var own = () => _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest), TestCaller.Alice);
        (await own.Should().ThrowAsync<AgentApprovalDeniedException>()).Which.ErrorCode.Should().Be("approver_must_differ");

        var decided = await _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest), TestCaller.Bob);
        decided.Status.Should().Be(AgentApprovalStatus.Approved);
    }

    [Fact]
    public async Task AnApproval_CanBeUsedOnce_ASecondDecisionAndAReplayedJobChangeNothing()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);
        await _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest));

        var twice = () => _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest));
        (await twice.Should().ThrowAsync<AgentApprovalConflictException>()).Which.ErrorCode.Should().Be("approval_not_pending");

        await _fx.RunAsync(TenantA, runId);
        await _fx.RunAsync(TenantA, runId); // duplicate delivery
        await _fx.RunAsync(TenantA, runId);

        var afterUse = () => _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest));
        await afterUse.Should().ThrowAsync<AgentApprovalConflictException>();
        _fx.World.Mail.Should().ContainSingle();
        _fx.World.Executions(SendMailTool.Name).Should().Be(1);
    }

    [Fact]
    public async Task Approve_WithStaleVersion_IsRejected_AndNothingChanges()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);

        var act = () => _fx.SendAsync(TenantA,
            new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest, Version: approval.Version + 5));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Pending);
        (await _fx.RunOfAsync(TenantA, runId)).Status.Should().Be(AgentRunStatus.AwaitingApproval);
    }

    [Fact]
    public async Task ARequest_NobodyDecidedInTime_CannotBeApprovedAnymore_AndExpiresTheRun()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);

        // Assert on this request only: the fixture's clock and tenant are shared, so requests left by other tests may
        // already be due and are expired by the same sweep.
        await _fx.ExpireApprovalsAsync(TenantA);
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Pending, "it is not due yet");
        _fx.Clock.Advance(TimeSpan.FromHours(73));

        var late = () => _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest));
        (await late.Should().ThrowAsync<AgentApprovalConflictException>()).Which.ErrorCode.Should().Be("approval_expired");

        (await _fx.ExpireApprovalsAsync(TenantA)).Should().BeGreaterThan(0);

        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Expired);
        var run = await _fx.RunOfAsync(TenantA, runId);
        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureCode.Should().Be("approval_expired");
        _fx.World.Mail.Should().BeEmpty();
    }

    [Fact]
    public async Task Approval_DoesNotReplaceTheInitiatorsPermission_RevokedWhileWaiting_TheActionIsRefused()
    {
        var runId = await SuspendedRunAsync(TestCaller.Alice);
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);
        await _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest), TestCaller.Bob);
        _fx.Caller.DeniedTo.Add((TestCaller.Alice, TenantPermissions.WebhooksManage)); // revoked after the approval

        await _fx.RunAsync(TenantA, runId);

        var call = (await _fx.StepsOfAsync(TenantA, runId)).Single(s => s.Kind == AgentRunStepKind.ToolCall);
        call.Status.Should().Be(AgentRunStepStatus.Failed);
        call.ErrorCode.Should().Be("forbidden");
        _fx.World.Mail.Should().BeEmpty();
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Cancelled,
            "it was never used, and it is closed when the run finishes");
    }

    [Fact]
    public async Task AnInitiator_RemovedWhileTheRunWaited_StopsTheRun_EvenAfterAnApproval()
    {
        var runId = await SuspendedRunAsync(TestCaller.Alice);
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);
        await _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest), TestCaller.Bob);
        _fx.Caller.NotMembers.Add(TestCaller.Alice);

        await _fx.RunAsync(TenantA, runId);

        (await _fx.RunOfAsync(TenantA, runId)).FailureCode.Should().Be("initiator_no_longer_authorized");
        _fx.World.Mail.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellingAWaitingRun_ClosesItsRequest_WhichCanThenNoLongerBeApproved()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);

        var cancelled = await _fx.SendAsync(TenantA, new CancelAgentRunCommand(runId));

        cancelled.Status.Should().Be(AgentRunStatus.Cancelled);
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Cancelled);
        var late = () => _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest));
        await late.Should().ThrowAsync<AgentApprovalConflictException>();
        _fx.World.Mail.Should().BeEmpty();
    }

    [Fact]
    public async Task ACancel_ThatArrivesJustBeforeTheRunWouldBeSuspended_Wins_AndNobodyIsAskedToApprove()
    {
        // The cancel request lands while the first tool runs; the next call would need an approval.
        _fx.Board.Play(
            ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}"""),
            ScriptedReply.Call(SendMailTool.Name, MailArguments));
        var started = await _fx.StartAsync(TenantA);
        var armed = false;
        _fx.Board.Next = turn =>
        {
            var reply = turn.TurnNumber == 1
                ? ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}""")
                : ScriptedReply.Call(SendMailTool.Name, MailArguments);
            if (turn.TurnNumber == 2 && !armed)
            {
                armed = true;
                // After the loop read the run for this iteration, before the gate saves.
                _fx.AdminExecAsync($"update agentic.agent_runs set cancel_requested = true where id = '{started.Id}'")
                    .GetAwaiter().GetResult();
            }

            return reply;
        };

        await _fx.RunAsync(TenantA, started.Id);

        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Cancelled);
        (await _fx.AdminScalarAsync<long>($"select count(*) from agentic.agent_approvals where run_id = '{started.Id}'")).Should().Be(0);
        _fx.Notifications.InApp.Should().NotContain(n => n.Content.Category == "agents.approval-required");
        _fx.World.Mail.Should().BeEmpty();
    }

    [Fact]
    public async Task AnApprovedRequest_ThatWasNeverUsed_IsClosed_WhenItsRunFinishesAnotherWay()
    {
        var runId = await SuspendedRunAsync(TestCaller.Alice);
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);
        await _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest), TestCaller.Bob);
        _fx.Caller.NotMembers.Add(TestCaller.Alice); // the run will fail instead of using the approval

        await _fx.RunAsync(TenantA, runId);

        (await _fx.RunOfAsync(TenantA, runId)).Status.Should().Be(AgentRunStatus.Failed);
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Cancelled);
    }

    [Fact]
    public async Task ARequest_CannotBeSeenOrDecided_FromAnotherTenant()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);

        var read = () => _fx.SendAsync(TenantB, new GetAgentApprovalByIdQuery(approval.Id));
        var decide = () => _fx.SendAsync(TenantB, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest));

        await read.Should().ThrowAsync<AgentApprovalNotFoundException>();
        await decide.Should().ThrowAsync<AgentApprovalNotFoundException>();
        (await _fx.SendAsync(TenantB, new GetAgentApprovalsQuery())).Result.Should().NotContain(a => a.Id == approval.Id);
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Pending);
    }

    [Fact]
    public async Task Requests_AreListedForDeciders_AndForTheInitiator_ButNotForOtherMembersOrApiKeys()
    {
        var runId = await SuspendedRunAsync(TestCaller.Alice);
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);
        _fx.Caller.Denied.Add(TenantPermissions.AgentApprovalsDecide); // nobody is a decider now

        var forAlice = await _fx.SendAsync(TenantA, new GetAgentApprovalsQuery { Status = AgentApprovalStatus.Pending, RunId = runId });
        forAlice.Result.Should().ContainSingle().Which.ArgumentsJson.Should().Be(MailArguments);

        var forBob = () => _fx.SendAsync(TenantA, new GetAgentApprovalByIdQuery(approval.Id), TestCaller.Bob);
        await forBob.Should().ThrowAsync<AgentApprovalNotFoundException>();

        var forKey = await _fx.SendAsync(TenantA, new GetAgentApprovalsQuery { RunId = runId },
            apiKeyScopes: [TenantPermissions.AgentRunsStart]);
        forKey.Result.Should().BeEmpty("a decision is for a person");
    }

    [Fact]
    public async Task AnApprovedIdempotentAction_InterruptedByAFailure_RunsAgainUnderTheSameApproval_WithOneEffect()
    {
        // Reading the document taints the run, so the (idempotent) note needs a person.
        _fx.Board.Play(
            ScriptedReply.Call(ReadDocumentTool.Name, """{"code":"D-1"}"""),
            ScriptedReply.Call(CreateNoteTool.Name, """{"title":"approved note"}"""));
        var started = await _fx.StartAsync(TenantA);
        await _fx.RunAsync(TenantA, started.Id);
        var approval = await _fx.ApprovalOfAsync(TenantA, started.Id);
        await _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest), TestCaller.Bob);
        _fx.World.FailOnce = (CreateNoteTool.Name, new TimeoutException("the database went away"));

        await _fx.RunAsync(TenantA, started.Id); // fails inside the tool, after the approval was used
        (await _fx.ApprovalOfAsync(TenantA, started.Id)).Status.Should().Be(AgentApprovalStatus.Consumed);
        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Queued);

        await _fx.RunAsync(TenantA, started.Id);

        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Completed);
        _fx.World.Notes.Values.Should().Equal("approved note");
        _fx.World.Observations.Where(o => o.Tool == CreateNoteTool.Name).Select(o => o.IdempotencyKey).Distinct()
            .Should().ContainSingle();
    }

    [Fact]
    public async Task ADecision_OnARunThatIsNoLongerWaiting_IsRefused()
    {
        var runId = await SuspendedRunAsync();
        var approval = await _fx.ApprovalOfAsync(TenantA, runId);
        // The run was failed by an operator while the request stayed pending.
        await _fx.AdminExecAsync($"update agentic.agent_runs set status = 'Failed' where id = '{runId}'");

        var act = () => _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest));

        (await act.Should().ThrowAsync<AgentApprovalConflictException>()).Which.ErrorCode.Should().Be("run_not_waiting");
        (await _fx.ApprovalOfAsync(TenantA, runId)).Status.Should().Be(AgentApprovalStatus.Pending, "nothing was saved");
        _fx.Jobs.For(runId).Should().ContainSingle("no job is queued for a decision that was not recorded");
    }
}
