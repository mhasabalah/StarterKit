using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Runtime.Execution;
using Hangfire.States;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>
/// Durability: the run row and its steps are the only state. Whatever stops an attempt (a provider outage, a failing
/// tool, a crashed worker, a duplicate delivery), the next one continues from the last checkpoint and business effects
/// are not duplicated.
/// </summary>
[Collection(AgenticPostgresCollection.Name)]
public class AgentRunRecoveryTests
{
    private readonly AgenticPostgresFixture _fx;

    public AgentRunRecoveryTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
    }

    [Fact]
    public async Task ADuplicateDelivery_OfAFinishedRun_DoesNothing()
    {
        _fx.Board.Play(ScriptedReply.Call(CreateNoteTool.Name, """{"title":"once"}"""));
        var started = await _fx.StartAsync(TenantA);
        await _fx.RunAsync(TenantA, started.Id);
        var turns = _fx.Board.Turns;

        await _fx.RunAsync(TenantA, started.Id);
        await _fx.RunAsync(TenantA, started.Id);

        _fx.Board.Turns.Should().Be(turns);
        _fx.World.Notes.Should().ContainSingle();
        (await _fx.StepsOfAsync(TenantA, started.Id)).Should().HaveCount(3);
    }

    [Fact]
    public async Task ASecondWorker_ThatPicksUpTheSameRun_WhileTheFirstHoldsTheLease_DoesNothing()
    {
        _fx.Board.Play(ScriptedReply.Call(CreateNoteTool.Name, """{"title":"once"}"""));
        var started = await _fx.StartAsync(TenantA);
        var nested = false;
        _fx.World.AfterTool = async _ =>
        {
            if (nested)
                return;
            nested = true;
            await _fx.RunAsync(TenantA, started.Id); // the duplicate delivery arrives while the tool is running
        };

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Completed);
        run.Attempts.Should().Be(1);
        _fx.World.Executions(CreateNoteTool.Name).Should().Be(1);
        _fx.World.Notes.Should().ContainSingle();
    }

    [Fact]
    public async Task AnInfrastructureFailure_InAnIdempotentTool_FailsTheAttempt_AndTheRetryRepeatsTheSameCall_WithTheSameKey()
    {
        _fx.Board.Play(ScriptedReply.Call(CreateNoteTool.Name, """{"title":"retried"}"""));
        _fx.World.FailOnce = (CreateNoteTool.Name, new TimeoutException("the database went away"));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var afterFailure = await _fx.RunOfAsync(TenantA, started.Id);
        afterFailure.Status.Should().Be(AgentRunStatus.Queued);
        afterFailure.ConsecutiveFailures.Should().Be(1);
        afterFailure.LeaseOwner.Should().BeNull();
        var retry = _fx.Jobs.For(started.Id).Should().Contain(j => j.IsScheduled).Subject;
        ((ScheduledState)retry.State).EnqueueAt.Should().BeAfter(DateTime.UtcNow);
        (await _fx.StepsOfAsync(TenantA, started.Id)).Single(s => s.Kind == AgentRunStepKind.ToolCall)
            .Status.Should().Be(AgentRunStepStatus.Executing);
        var turnsBeforeRetry = _fx.Board.Turns;

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Completed);
        run.Attempts.Should().Be(2);
        run.ConsecutiveFailures.Should().Be(0);
        var call = (await _fx.StepsOfAsync(TenantA, started.Id)).Single(s => s.Kind == AgentRunStepKind.ToolCall);
        call.Status.Should().Be(AgentRunStepStatus.Succeeded);
        call.Executions.Should().Be(2);
        // Resumed from the checkpoint: the model turn that planned the call was not asked again.
        (_fx.Board.Turns - turnsBeforeRetry).Should().Be(1);
        // One business effect, addressed by one idempotency key across both executions.
        _fx.World.Notes.Should().ContainSingle();
        _fx.World.Observations.Where(o => o.Tool == CreateNoteTool.Name).Select(o => o.IdempotencyKey).Distinct()
            .Should().ContainSingle();
    }

    [Fact]
    public async Task AnUnexpectedFailure_InANonIdempotentTool_StopsTheRun_InsteadOfRepeatingTheAction()
    {
        _fx.Board.Play(ScriptedReply.Call(AppendLogTool.Name, """{"title":"line"}"""));
        _fx.World.FailOnce = (AppendLogTool.Name, new TimeoutException("connection reset: secret-host-10.0.0.7"));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureCode.Should().Be("tool_outcome_unknown");
        run.FailureMessage.Should().Contain(AppendLogTool.Name).And.NotContain("secret-host");
        _fx.Jobs.For(started.Id).Should().NotContain(j => j.IsScheduled);
        _fx.World.Executions(AppendLogTool.Name).Should().Be(1);
    }

    [Fact]
    public async Task ANonIdempotentTool_FoundInterruptedAfterACrash_IsNotRunAgain()
    {
        _fx.Board.Play(ScriptedReply.Call(AppendLogTool.Name, """{"title":"line"}"""));
        var started = await _fx.StartAsync(TenantA);
        // The worker died inside the tool: the step was checkpointed as Executing, the lease expired, the row is still
        // released, and the run is queued again.
        using (var shutdown = new CancellationTokenSource())
        {
            _fx.World.AfterTool = _ =>
            {
                shutdown.Cancel();
                throw new OperationCanceledException(shutdown.Token);
            };
            var crashed = () => _fx.RunAsync(TenantA, started.Id, shutdown.Token);
            await crashed.Should().ThrowAsync<OperationCanceledException>();
        }

        _fx.World.AfterTool = null;
        (await _fx.StepsOfAsync(TenantA, started.Id)).Single(s => s.Kind == AgentRunStepKind.ToolCall)
            .Status.Should().Be(AgentRunStepStatus.Executing);
        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Queued);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureCode.Should().Be("tool_outcome_unknown");
        _fx.World.Executions(AppendLogTool.Name).Should().Be(1, "the action is never repeated");
    }

    [Fact]
    public async Task AShutdown_InTheMiddleOfAnIdempotentTool_GivesTheRunBack_WithoutCountingAFailure_AndItResumes()
    {
        _fx.Board.Play(ScriptedReply.Call(CreateNoteTool.Name, """{"title":"survives a restart"}"""));
        var started = await _fx.StartAsync(TenantA);
        using (var shutdown = new CancellationTokenSource())
        {
            _fx.World.AfterTool = _ =>
            {
                shutdown.Cancel();
                throw new OperationCanceledException(shutdown.Token);
            };
            var interrupted = () => _fx.RunAsync(TenantA, started.Id, shutdown.Token);
            await interrupted.Should().ThrowAsync<OperationCanceledException>();
        }

        _fx.World.AfterTool = null;
        var released = await _fx.RunOfAsync(TenantA, started.Id);
        released.Status.Should().Be(AgentRunStatus.Queued);
        released.ConsecutiveFailures.Should().Be(0);
        released.LeaseOwner.Should().BeNull();

        await _fx.RunAsync(TenantA, started.Id);

        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Completed);
        _fx.World.Notes.Values.Should().Equal("survives a restart");
    }

    [Fact]
    public async Task AProviderOutage_IsRetriedWithBackoff_ThenFailsTheRun_WithoutLeakingTheProvidersError()
    {
        _fx.Options.MaxAttempts = 2;
        _fx.Board.Next = _ => throw new HttpRequestException("401 from https://provider.example.com key=sk-live-123");
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var first = await _fx.RunOfAsync(TenantA, started.Id);
        first.Status.Should().Be(AgentRunStatus.Queued);
        first.ConsecutiveFailures.Should().Be(1);
        _fx.Jobs.For(started.Id).Should().ContainSingle(j => j.IsScheduled);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureCode.Should().Be("model_unavailable");
        run.FailureMessage.Should().NotContain("sk-live").And.NotContain("provider.example.com");
        _fx.Jobs.For(started.Id).Count(j => j.IsScheduled).Should().Be(1, "no retry is scheduled after the last attempt");
    }

    [Fact]
    public async Task AProviderOutage_ThatEnds_LetsTheRunComplete_AndTheFailureCountStartsOver()
    {
        var calls = 0;
        _fx.Board.Next = _ => ++calls == 1 ? throw new HttpRequestException("503") : ScriptedReply.Say("Back again.");
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);
        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Completed);
        run.OutputText.Should().Be("Back again.");
        run.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task ARun_WhoseJobWasLost_IsQueuedAgain_ByTheSweep_OnlyAfterTheQuietPeriod()
    {
        _fx.Board.Play(ScriptedReply.Say("Recovered."));
        var started = await _fx.StartAsync(TenantA);
        _fx.Jobs.For(started.Id).Should().ContainSingle();

        await _fx.SweepAsync(TenantA);
        _fx.Jobs.For(started.Id).Should().ContainSingle("a run that was queued a moment ago is not stalled");

        _fx.Clock.Advance(TimeSpan.FromMinutes(11));
        await _fx.SweepAsync(TenantA);

        _fx.Jobs.For(started.Id).Should().HaveCount(2);
        await _fx.SweepAsync(TenantA);
        _fx.Jobs.For(started.Id).Should().HaveCount(2, "the sweep stamped the run, so it is not queued a third time at once");

        await _fx.RunAsync(TenantA, started.Id);
        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task ARun_LeftRunning_ByADeadWorker_IsTakenOver_OnlyAfterItsLeaseExpired()
    {
        _fx.Board.Play(ScriptedReply.Call(CreateNoteTool.Name, """{"title":"taken over"}"""));
        var started = await _fx.StartAsync(TenantA);
        // A worker claimed the run and died: the row says Running under a lease nobody will renew or release.
        var leaseUntil = _fx.Clock.GetUtcNow().AddMinutes(5).UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await _fx.AdminExecAsync(
            "update agentic.agent_runs set status = 'Running', lease_owner = 'dead-worker:1', attempts = 1, " +
            $"lease_until = '{leaseUntil}' where id = '{started.Id}'");

        await _fx.RunAsync(TenantA, started.Id);
        await _fx.SweepAsync(TenantA);
        (await _fx.RunOfAsync(TenantA, started.Id)).LeaseOwner.Should().Be("dead-worker:1", "a live lease is respected");
        _fx.Board.Turns.Should().Be(0);
        _fx.Jobs.For(started.Id).Should().ContainSingle();

        _fx.Clock.Advance(TimeSpan.FromMinutes(20));
        (await _fx.SweepAsync(TenantA)).Should().BeGreaterThan(0);
        _fx.Jobs.For(started.Id).Should().HaveCount(2);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Completed);
        run.Attempts.Should().Be(2);
        _fx.World.Notes.Values.Should().Equal("taken over");
    }

    [Fact]
    public async Task TheSweep_NeverQueuesAFinishedRun_OrAnotherTenantsRun()
    {
        var finished = await _fx.StartAsync(TenantA);
        await _fx.RunAsync(TenantA, finished.Id);
        var other = await _fx.StartAsync(TenantB);
        _fx.Clock.Advance(TimeSpan.FromMinutes(11));

        await _fx.SweepAsync(TenantA);

        _fx.Jobs.For(finished.Id).Should().ContainSingle();
        _fx.Jobs.For(other.Id).Should().ContainSingle("tenant A's sweep cannot even see tenant B's run");
    }

    [Fact]
    public async Task ARun_WhoseAgentNoLongerExists_FailsCleanly()
    {
        var started = await _fx.StartAsync(TenantA);
        await _fx.AdminExecAsync($"update agentic.agent_runs set agent_key = 'test.removed' where id = '{started.Id}'");

        await _fx.RunAsync(TenantA, started.Id);

        (await _fx.RunOfAsync(TenantA, started.Id)).FailureCode.Should().Be("agent_not_found");
    }

    [Fact]
    public void RetryDelay_GrowsAndIsCapped()
    {
        AgentRunExecutor.RetryDelay(1).Should().Be(TimeSpan.FromSeconds(15));
        AgentRunExecutor.RetryDelay(2).Should().Be(TimeSpan.FromMinutes(1));
        AgentRunExecutor.RetryDelay(3).Should().Be(TimeSpan.FromMinutes(4));
        AgentRunExecutor.RetryDelay(10).Should().Be(TimeSpan.FromMinutes(15));
    }
}
