using Agentic.Contracts.Agents;
using Agentic.Contracts.Runs;
using Agentic.Domains.AgentRuns.Events;
using Agentic.Domains.AgentRuns.Exceptions;
using Agentic.Domains.AgentRuns.Models;

namespace Agentic.Tests;

public class AgentRunTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly Guid User = Guid.Parse("11111111-aaaa-0000-0000-000000000001");

    private static AgentRun NewRun(AgentRunBudget? budget = null, IEnumerable<string>? ceiling = null) =>
        AgentRun.Start("test.assistant", 1, "default", AgentAutonomy.Prepare, AgentTriggerKind.User, User, "Do it", null,
            budget ?? new AgentRunBudget(3, 2, 1_000, 60), "ar-EG", ceiling);

    private static AgentRun Claimed(AgentRunBudget? budget = null)
    {
        var run = NewRun(budget);
        run.TryClaim("worker-1", Now, Lease).Should().BeTrue();
        return run;
    }

    [Fact]
    public void Start_CreatesAQueuedRun_WithNormalizedCultureAndSortedCeiling()
    {
        var run = NewRun(ceiling: ["b.write", "a.read", "a.read"]);

        run.Status.Should().Be(AgentRunStatus.Queued);
        run.Id.Should().NotBeEmpty();
        run.Culture.Should().Be("ar");
        run.PermissionCeiling.Should().Equal("a.read", "b.write");
        run.CorrelationId.Should().Be($"agent-run:{run.Id:D}");
        run.NextSequence.Should().Be(1);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", "")]
    public void Start_WithoutPromptOrInput_IsRejected(string? prompt, string? input)
    {
        var act = () => AgentRun.Start("test.assistant", 1, "default", AgentAutonomy.Observe, AgentTriggerKind.User, User,
            prompt, input, new AgentRunBudget(1, 1, 1, 1), "en");

        act.Should().Throw<InvalidAgentRunStateException>();
    }

    [Fact]
    public void Start_WithoutAnInitiator_IsRejected()
    {
        var act = () => AgentRun.Start("test.assistant", 1, "default", AgentAutonomy.Observe, AgentTriggerKind.User,
            Guid.Empty, "x", null, new AgentRunBudget(1, 1, 1, 1), "en");

        act.Should().Throw<InvalidAgentRunStateException>();
    }

    [Fact]
    public void TryClaim_ByASecondWorker_IsRefused_WhileTheLeaseIsLive_AndAllowedOnceItExpired()
    {
        var run = Claimed();

        run.TryClaim("worker-2", Now.AddMinutes(1), Lease).Should().BeFalse();
        run.LeaseOwner.Should().Be("worker-1");

        run.TryClaim("worker-2", Now.AddMinutes(6), Lease).Should().BeTrue();
        run.LeaseOwner.Should().Be("worker-2");
        run.HoldsLease("worker-1").Should().BeFalse();
        run.Attempts.Should().Be(2);
    }

    [Fact]
    public void TryClaim_OnAFinishedRun_IsRefused()
    {
        var run = Claimed();
        run.Fail("internal_error", "x", Now);

        run.TryClaim("worker-2", Now, Lease).Should().BeFalse();
    }

    [Fact]
    public void RecordModelTurn_CountsUsage_AndNumbersTheSteps()
    {
        var run = Claimed();

        var turn = run.RecordModelTurn("thinking", "model-x", "tool_calls", 100, 20, 0.5m, 1_500, Now);
        var call = run.PlanToolCall(turn, "call-1", "test.echo", """{"text":"a"}""");

        turn.Sequence.Should().Be(1);
        turn.Kind.Should().Be(AgentRunStepKind.ModelTurn);
        call.Sequence.Should().Be(2);
        call.TurnSequence.Should().Be(1);
        call.Status.Should().Be(AgentRunStepStatus.Pending);
        run.ModelTurns.Should().Be(1);
        run.TotalTokens.Should().Be(120);
        run.EstimatedCost.Should().Be(0.5m);
        run.ActiveMilliseconds.Should().Be(1_500);
        run.NextSequence.Should().Be(3);
    }

    [Fact]
    public void ExhaustedBudget_NamesTheLimitThatWasReached()
    {
        var turns = Claimed(new AgentRunBudget(1, 5, 10_000, 60));
        turns.RecordModelTurn(null, null, null, 1, 1, 0, 1, Now);
        turns.ExhaustedBudget().Should().Be("max_model_turns");

        var tokens = Claimed(new AgentRunBudget(5, 5, 100, 60));
        tokens.RecordModelTurn(null, null, null, 90, 10, 0, 1, Now);
        tokens.ExhaustedBudget().Should().Be("max_tokens");

        var duration = Claimed(new AgentRunBudget(5, 5, 10_000, 1));
        duration.RecordModelTurn(null, null, null, 1, 1, 0, 1_000, Now);
        duration.ExhaustedBudget().Should().Be("max_duration");

        Claimed().ExhaustedBudget().Should().BeNull();
    }

    [Fact]
    public void RecordToolCall_CountsOnlyExecutedCalls_AndTaintsTheRunForGood()
    {
        var run = Claimed();
        var turn = run.RecordModelTurn(null, null, null, 1, 1, 0, 0, Now);
        var refused = run.PlanToolCall(turn, "c1", "test.echo", "{}");
        var executed = run.PlanToolCall(turn, "c2", "test.read_document", "{}");

        refused.FailWith("forbidden", "{}", 0, Now);
        run.RecordToolCall(refused, executed: false, untrustedContent: false);
        executed.MarkExecuting(1, Now);
        executed.Succeed("{}", 40, Now);
        run.RecordToolCall(executed, executed: true, untrustedContent: true);

        run.ToolCalls.Should().Be(1);
        run.Tainted.Should().BeTrue();
        run.ToolCallBudgetExhausted.Should().BeFalse();
    }

    [Fact]
    public void ReleaseForRetry_CountsConsecutiveFailures_AndACheckpointResetsThem()
    {
        var run = Claimed();

        run.ReleaseForRetry(Now).Should().Be(1);
        run.Status.Should().Be(AgentRunStatus.Queued);
        run.LeaseOwner.Should().BeNull();

        run.TryClaim("worker-1", Now, Lease);
        run.ReleaseForRetry(Now).Should().Be(2);

        run.TryClaim("worker-1", Now, Lease);
        run.RecordModelTurn(null, null, null, 1, 1, 0, 0, Now);
        run.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void RequestCancel_OnAQueuedRun_CancelsAtOnce_AndOnARunningOneAtTheNextCheckpoint()
    {
        var queued = NewRun();
        queued.RequestCancel(Now);
        queued.Status.Should().Be(AgentRunStatus.Cancelled);

        var running = Claimed();
        running.RequestCancel(Now);
        running.Status.Should().Be(AgentRunStatus.Running);
        running.CancelRequested.Should().BeTrue();

        running.ConfirmCancelled(Now);
        running.Status.Should().Be(AgentRunStatus.Cancelled);
        running.CancelRequested.Should().BeFalse();
    }

    [Fact]
    public void Complete_FinishesTheRun_ReleasesTheLease_AndRaisesTheFinishedEvent()
    {
        var run = Claimed();

        run.Complete("""{"title":"t"}""", "done", Now);

        run.Status.Should().Be(AgentRunStatus.Completed);
        run.IsTerminal.Should().BeTrue();
        run.FinishedAt.Should().Be(Now);
        run.LeaseOwner.Should().BeNull();
        run.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<AgentRunFinishedEvent>()
            .Which.Status.Should().Be(AgentRunStatus.Completed);
    }

    [Fact]
    public void AFinishedRun_CannotBeFinishedAgain()
    {
        var run = Claimed();
        run.Fail("internal_error", "boom", Now);

        var complete = () => run.Complete(null, "x", Now);
        var cancel = () => run.RequestCancel(Now);

        complete.Should().Throw<InvalidAgentRunStateException>();
        cancel.Should().Throw<InvalidAgentRunStateException>();
        run.FailureCode.Should().Be("internal_error");
    }

    [Fact]
    public void IsStalled_IsTrue_ForAQueuedRunThatWasNotQueuedRecently_AndFalseUnderALiveLease()
    {
        var run = NewRun();
        run.MarkEnqueued(Now);
        var quiet = TimeSpan.FromMinutes(10);

        run.IsStalled(Now.AddMinutes(5), quiet).Should().BeFalse();
        run.IsStalled(Now.AddMinutes(11), quiet).Should().BeTrue();

        run.TryClaim("worker-1", Now.AddMinutes(11), Lease);
        run.IsStalled(Now.AddMinutes(12), quiet).Should().BeFalse();
        run.IsStalled(Now.AddMinutes(30), quiet).Should().BeTrue();
    }

    [Fact]
    public void AClosedStep_CannotBeChangedAgain()
    {
        var run = Claimed();
        var turn = run.RecordModelTurn(null, null, null, 1, 1, 0, 0, Now);
        var step = run.PlanToolCall(turn, "c1", "test.echo", "{}");
        step.MarkExecuting(1, Now);
        step.Succeed("{}", 5, Now);

        var again = () => step.FailWith("forbidden", "{}", 0, Now);

        again.Should().Throw<InvalidAgentRunStateException>();
        step.Status.Should().Be(AgentRunStepStatus.Succeeded);
        step.Executions.Should().Be(1);
    }
}
