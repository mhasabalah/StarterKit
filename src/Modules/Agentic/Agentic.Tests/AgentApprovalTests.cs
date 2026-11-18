using Agentic.Contracts.Agents;
using Agentic.Contracts.Approvals;
using Agentic.Contracts.Runs;
using Agentic.Contracts.Tools;
using Agentic.Domains.AgentRuns.EventHandlers;
using Agentic.Domains.AgentRuns.Events;
using Agentic.Domains.AgentRuns.Exceptions;
using Agentic.Domains.AgentRuns.Models;
using Agentic.Domains.AgentRuns.Services;
using Agentic.Domains.Approvals.Events;
using Agentic.Domains.Approvals.Exceptions;
using Agentic.Domains.Approvals.Models;
using Agentic.Domains.Policies.Models;
using Agentic.Options;
using Agentic.Runtime.Execution;

namespace Agentic.Tests;

public class AgentApprovalTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan TimeToLive = TimeSpan.FromHours(72);
    private static readonly Guid Alice = Guid.Parse("11111111-aaaa-0000-0000-000000000001");
    private static readonly Guid Bob = Guid.Parse("22222222-bbbb-0000-0000-000000000002");
    private const string Arguments = """{"title":"Invoice 1045"}""";

    private static (AgentRun Run, AgentRunStep Step) RunWithCall()
    {
        var run = AgentRun.Start("test.assistant", 1, "default", AgentAutonomy.Execute, AgentTriggerKind.User, Alice,
            "Send it", null, new AgentRunBudget(5, 5, 1_000, 60), "ar");
        run.TryClaim("worker", Now, TimeSpan.FromMinutes(5));
        var turn = run.RecordModelTurn("I will send the invoice.", "m", "tool_calls", 1, 1, 0, 0, Now);
        return (run, run.PlanToolCall(turn, "c1", "test.send_email", Arguments));
    }

    private static AgentApproval Pending()
    {
        var (run, step) = RunWithCall();
        return AgentApproval.Request(run, step, "test.send_email", 1, AgentToolRisk.ExternalOrIrreversible, "mail.send",
            Arguments, "I will send the invoice.", Now, TimeToLive);
    }

    [Fact]
    public void Request_IsPending_BoundToThePayload_AndRaisesTheRequestedEvent()
    {
        var approval = Pending();

        approval.Status.Should().Be(AgentApprovalStatus.Pending);
        approval.ArgumentsJson.Should().Be(Arguments);
        approval.PayloadDigest.Should().Be(AgentApproval.HashOf("test.send_email", 1, Arguments)).And.HaveLength(64);
        approval.RequestedBy.Should().Be(Alice);
        approval.ExpiresAt.Should().Be(Now + TimeToLive);
        approval.Culture.Should().Be("ar");
        approval.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<AgentApprovalRequestedEvent>()
            .Which.ToolName.Should().Be("test.send_email");
    }

    [Fact]
    public void TheHash_ChangesWithTheTool_ItsVersion_AndEveryCharacterOfTheArguments()
    {
        var hash = AgentApproval.HashOf("test.send_email", 1, Arguments);

        AgentApproval.HashOf("test.send_mail", 1, Arguments).Should().NotBe(hash);
        AgentApproval.HashOf("test.send_email", 2, Arguments).Should().NotBe(hash);
        AgentApproval.HashOf("test.send_email", 1, Arguments.Replace("1045", "1046")).Should().NotBe(hash);
        AgentApproval.HashOf("test.send_email", 1, Arguments + " ").Should().NotBe(hash);
        // Length-prefixed: moving characters between the parts never collides.
        AgentApproval.HashOf("ab", 1, "c").Should().NotBe(AgentApproval.HashOf("a", 1, "bc"));
    }

    [Fact]
    public void Covers_OnlyTheExactToolVersionAndArguments()
    {
        var approval = Pending();

        approval.Covers("test.send_email", 1, Arguments).Should().BeTrue();
        approval.Covers("test.send_email", 2, Arguments).Should().BeFalse();
        approval.Covers("test.send_email", 1, """{"title":"Pay the attacker"}""").Should().BeFalse();
    }

    [Fact]
    public void Approve_RecordsWhoAndWhen_AndCanBeConsumedExactlyOnce()
    {
        var approval = Pending();

        approval.Approve(Bob, "  Looks right  ", Now.AddMinutes(5));
        approval.Status.Should().Be(AgentApprovalStatus.Approved);
        approval.DecidedBy.Should().Be(Bob);
        approval.DecisionNote.Should().Be("Looks right");
        approval.DomainEvents.OfType<AgentApprovalDecidedEvent>().Should().ContainSingle()
            .Which.Status.Should().Be(AgentApprovalStatus.Approved);

        approval.Consume();
        approval.Status.Should().Be(AgentApprovalStatus.Consumed);

        var again = () => approval.Consume();
        again.Should().Throw<InvalidAgentApprovalStateException>();
    }

    [Fact]
    public void ADecidedRequest_CannotBeDecidedAgain()
    {
        var approval = Pending();
        approval.Reject(Bob, "No", Now);

        var approve = () => approval.Approve(Bob, null, Now);
        var reject = () => approval.Reject(Bob, null, Now);

        approve.Should().Throw<AgentApprovalConflictException>().Which.ErrorCode.Should().Be("approval_not_pending");
        reject.Should().Throw<AgentApprovalConflictException>();
        approval.Status.Should().Be(AgentApprovalStatus.Rejected);
    }

    [Fact]
    public void AnExpiredRequest_CannotBeApproved_EvenBeforeTheExpiryJobRan()
    {
        var approval = Pending();

        var late = () => approval.Approve(Bob, null, Now + TimeToLive);

        late.Should().Throw<AgentApprovalConflictException>().Which.ErrorCode.Should().Be("approval_expired");
        approval.IsExpired(Now + TimeToLive).Should().BeTrue();
        approval.IsExpired(Now).Should().BeFalse();
    }

    [Fact]
    public void AnUnapprovedRequest_CannotBeConsumed()
    {
        var consume = () => Pending().Consume();

        consume.Should().Throw<InvalidAgentApprovalStateException>();
    }

    [Fact]
    public void Repropose_VoidsADecisionMadeForAnOlderToolVersion()
    {
        var approval = Pending();
        approval.Approve(Bob, "ok", Now);

        approval.Repropose(2, AgentToolRisk.ExternalOrIrreversible, "mail.send", Now.AddHours(1), TimeToLive);

        approval.Status.Should().Be(AgentApprovalStatus.Pending);
        approval.ToolVersion.Should().Be(2);
        approval.DecidedBy.Should().BeNull();
        approval.DecisionNote.Should().BeNull();
        approval.Covers("test.send_email", 2, Arguments).Should().BeTrue();
        approval.Covers("test.send_email", 1, Arguments).Should().BeFalse();
        approval.ExpiresAt.Should().Be(Now.AddHours(1) + TimeToLive);
    }

    [Fact]
    public void Cancel_ClosesAnOpenRequest_AndLeavesADecidedOrUsedOneAsItIs()
    {
        var open = Pending();
        open.Cancel();
        open.Status.Should().Be(AgentApprovalStatus.Cancelled);

        var used = Pending();
        used.Approve(Bob, null, Now);
        used.Consume();
        used.Cancel();
        used.Status.Should().Be(AgentApprovalStatus.Consumed);
    }

    // ----- suspension of the run and the step ----------------------------------------------------------------------

    [Fact]
    public void ASuspendedRun_HoldsNoLease_CannotBeClaimed_AndGoesBackToTheQueueOnResume()
    {
        var (run, step) = RunWithCall();

        step.AwaitApproval(1);
        run.Suspend();

        step.Status.Should().Be(AgentRunStepStatus.AwaitingApproval);
        step.IsOpen.Should().BeTrue("the run cannot ask the model again before the call is closed");
        run.Status.Should().Be(AgentRunStatus.AwaitingApproval);
        run.LeaseOwner.Should().BeNull();
        run.TryClaim("another", Now, TimeSpan.FromMinutes(5)).Should().BeFalse();
        run.IsStalled(Now.AddDays(3), TimeSpan.FromMinutes(10)).Should().BeFalse("it waits for a person, not for a worker");

        run.Resume(Now.AddHours(1));
        run.Status.Should().Be(AgentRunStatus.Queued);
        run.TryClaim("another", Now.AddHours(1), TimeSpan.FromMinutes(5)).Should().BeTrue();

        var twice = () => run.Resume(Now);
        twice.Should().Throw<InvalidAgentRunStateException>();
    }

    [Fact]
    public void ARejectedStep_IsClosed_WithTheReasonForTheModel()
    {
        var (_, step) = RunWithCall();
        step.AwaitApproval(1);

        step.Reject("""{"error":{"code":"rejected"}}""", Now);

        step.Status.Should().Be(AgentRunStepStatus.Rejected);
        step.ErrorCode.Should().Be("rejected");
        step.Executions.Should().Be(0);
        step.IsOpen.Should().BeFalse();
    }

    // ----- policies and budgets ------------------------------------------------------------------------------------

    [Fact]
    public void ABudget_IsTheStrictestOfTheModuleTheAgentAndTheTenant()
    {
        var module = new AgentRunLimitOptions { MaxModelTurns = 12, MaxToolCalls = 30, MaxTokens = 200_000, MaxDurationSeconds = 600 };
        var agent = new AgentLimits(MaxModelTurns: 8, MaxToolCalls: 100, MaxTokens: 50_000, MaxDurationSeconds: 9_999);
        var tenant = new AgentPolicySettings(true, null, true, false, MaxModelTurns: 20, MaxToolCalls: 0, MaxTokens: 10_000);

        AgentRunStarter.Budget(module, agent, tenant).Should().Be(new AgentRunBudget(8, 0, 10_000, 600));
        AgentRunStarter.Budget(module, null).Should().Be(new AgentRunBudget(12, 30, 200_000, 600));
        AgentRunStarter.Budget(module, new AgentLimits(MaxModelTurns: 0), AgentPolicySettings.Default)
            .Should().Be(new AgentRunBudget(12, 30, 200_000, 600), "a non-positive request is ignored, never an unlimited run");
    }

    [Fact]
    public void APolicy_RejectsLimitsThatCouldNeverRun()
    {
        var act = () => AgentPolicy.Create("test.assistant", AgentPolicySettings.Default with { MaxModelTurns = 0 });

        act.Should().Throw<InvalidAgentRunStateException>();
    }

    [Fact]
    public void ARun_NeedsApprovalForChanges_WhenThePolicyDemandsIt_OrOnceItIsTainted()
    {
        var byPolicy = AgentRun.Start("test.assistant", 1, "default", AgentAutonomy.Execute, AgentTriggerKind.User, Alice,
            "x", null, new AgentRunBudget(5, 5, 1_000, 60), "en", approveAllChanges: true);
        var (plain, _) = RunWithCall();

        byPolicy.ChangesNeedApproval.Should().BeTrue();
        plain.ChangesNeedApproval.Should().BeFalse();
    }

    // ----- what leaves the module ----------------------------------------------------------------------------------

    [Fact]
    public void FinishingARun_RaisesTheIntegrationEvent_OfItsOutcome_AndACancelledRunRaisesNone()
    {
        var (completed, _) = RunWithCall();
        var (failed, _) = RunWithCall();
        var (cancelled, _) = RunWithCall();

        completed.Complete("""{"a":1}""", null, Now);
        failed.Fail("budget_exceeded", "limit", Now);
        cancelled.ConfirmCancelled(Now);

        completed.IntegrationEvents.Should().ContainSingle().Which.Should()
            .BeOfType<Contracts.Runs.IntegrationEvents.AgentRunCompletedIntegrationEvent>().Which.HasTypedOutput.Should().BeTrue();
        failed.IntegrationEvents.Should().ContainSingle().Which.Should()
            .BeOfType<Contracts.Runs.IntegrationEvents.AgentRunFailedIntegrationEvent>().Which.FailureCode.Should().Be("budget_exceeded");
        cancelled.IntegrationEvents.Should().BeEmpty();
    }

    [Fact]
    public void Notifications_AndProgress_CarryNamesAndStates_NeverThePayload()
    {
        var (run, step) = RunWithCall();
        var requested = new AgentApprovalRequestedEvent(Guid.NewGuid(), run.Id, run.AgentKey, "test.send_email", Alice, "ar");

        var inApp = AgentApprovalRequestedEventHandler.Content(requested);
        var finished = AgentRunFinishedEventHandler.Content(
            new AgentRunFinishedEvent(run.Id, run.AgentKey, AgentRunStatus.Failed, Alice, "en", "budget_exceeded"));
        var progress = AgentRunProgressPublisher.Payload(run, step);

        inApp.Category.Should().Be(AgentNotificationCategories.ApprovalRequired);
        inApp.Body.Ar.Should().Contain("test.send_email");
        (inApp.Body.En + inApp.Title.En).Should().NotContain("Invoice");
        finished.Data!["failureCode"].Should().Be("budget_exceeded");
        finished.Category.Should().Be(AgentNotificationCategories.RunFinished);
        progress["tool"].Should().Be("test.send_email");
        progress["status"].Should().Be((int)AgentRunStatus.Running);
        progress.Values.Select(v => v?.ToString()).Should().NotContain(v => v != null && v.Contains("Invoice"));
    }
}
