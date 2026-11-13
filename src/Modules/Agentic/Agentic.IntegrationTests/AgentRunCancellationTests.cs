using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Domains.AgentRuns.Exceptions;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy.Authorization;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>Cancelling a run: who may, and what happens to a waiting and to a running run.</summary>
[Collection(AgenticPostgresCollection.Name)]
public class AgentRunCancellationTests
{
    private readonly AgenticPostgresFixture _fx;

    public AgentRunCancellationTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
    }

    [Fact]
    public async Task AWaitingRun_IsCancelledAtOnce_AndItsQueuedJobThenDoesNothing()
    {
        var started = await _fx.StartAsync(TenantA);

        var cancelled = await _fx.SendAsync(TenantA, new CancelAgentRunCommand(started.Id));

        cancelled.Status.Should().Be(AgentRunStatus.Cancelled);
        cancelled.FinishedAt.Should().NotBeNull();

        await _fx.RunAsync(TenantA, started.Id);
        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Cancelled);
        _fx.Board.Turns.Should().Be(0);
    }

    [Fact]
    public async Task ARunningRun_StopsAtItsNextCheckpoint_AndWhatItCommittedStays()
    {
        _fx.Board.Next = _ => ScriptedReply.Call(CreateNoteTool.Name, """{"title":"before the cancel"}""");
        var started = await _fx.StartAsync(TenantA);
        AgentRunDto? answer = null;
        _fx.World.AfterTool = async _ =>
        {
            _fx.World.AfterTool = null;
            answer = await _fx.SendAsync(TenantA, new CancelAgentRunCommand(started.Id)); // the user cancels meanwhile
        };

        await _fx.RunAsync(TenantA, started.Id);

        answer!.Status.Should().Be(AgentRunStatus.Running, "a worker holds the run: the request is recorded, not forced");
        answer.CancelRequested.Should().BeTrue();
        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Cancelled);
        run.CancelRequested.Should().BeFalse();
        run.LeaseOwner.Should().BeNull();
        _fx.World.Executions(CreateNoteTool.Name).Should().Be(1, "no further tool runs after the request");
        _fx.World.Notes.Values.Should().Equal("before the cancel");
    }

    [Fact]
    public async Task SomeoneElsesRun_CannotBeCancelled_AndDoesNotExistForThem()
    {
        var started = await _fx.StartAsync(TenantA, user: TestCaller.Alice);
        _fx.Caller.DeniedTo.Add((TestCaller.Bob, TenantPermissions.AgentsManage));

        var asBob = () => _fx.SendAsync(TenantA, new CancelAgentRunCommand(started.Id), TestCaller.Bob);
        var fromTenantB = () => _fx.SendAsync(TenantB, new CancelAgentRunCommand(started.Id));

        await asBob.Should().ThrowAsync<AgentRunNotFoundException>();
        await fromTenantB.Should().ThrowAsync<AgentRunNotFoundException>();
        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Queued);
    }

    [Fact]
    public async Task AHolderOfTheManagePermission_MayCancelAnyRunOfTheTenant()
    {
        var started = await _fx.StartAsync(TenantA, user: TestCaller.Alice);

        var cancelled = await _fx.SendAsync(TenantA, new CancelAgentRunCommand(started.Id), TestCaller.Bob);

        cancelled.Status.Should().Be(AgentRunStatus.Cancelled);
        cancelled.InitiatedBy.Should().Be(TestCaller.Alice);
    }

    [Fact]
    public async Task AFinishedRun_CannotBeCancelled()
    {
        var started = await _fx.StartAsync(TenantA);
        await _fx.RunAsync(TenantA, started.Id);

        var act = () => _fx.SendAsync(TenantA, new CancelAgentRunCommand(started.Id));

        (await act.Should().ThrowAsync<InvalidAgentRunStateException>()).Which.ErrorCode.Should().Be("invalid_run_state");
        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task Cancel_WithStaleVersion_IsRejected_AndNothingChanges()
    {
        var started = await _fx.StartAsync(TenantA);

        var act = () => _fx.SendAsync(TenantA, new CancelAgentRunCommand(started.Id, started.Version + 7));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Queued);
    }

    [Fact]
    public async Task Cancel_WithTheCurrentVersion_Succeeds()
    {
        var started = await _fx.StartAsync(TenantA);

        var cancelled = await _fx.SendAsync(TenantA, new CancelAgentRunCommand(started.Id, started.Version));

        cancelled.Status.Should().Be(AgentRunStatus.Cancelled);
    }

    [Fact]
    public async Task AnApiKey_CancelsOnlyTheRunsItStarted()
    {
        var keyA = Guid.NewGuid();
        var keyB = Guid.NewGuid();
        string[] scopes = [TenantPermissions.AgentRunsStart];
        var started = await _fx.StartAsync(TenantA, apiKeyScopes: scopes, apiKeyId: keyA);

        var asOtherKey = () => _fx.SendAsync(TenantA, new CancelAgentRunCommand(started.Id), apiKeyScopes: scopes, apiKeyId: keyB);
        await asOtherKey.Should().ThrowAsync<AgentRunNotFoundException>();

        var cancelled = await _fx.SendAsync(TenantA, new CancelAgentRunCommand(started.Id), apiKeyScopes: scopes, apiKeyId: keyA);
        cancelled.Status.Should().Be(AgentRunStatus.Cancelled);
    }
}
