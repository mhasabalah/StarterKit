using System.Text.Json;
using Agentic.Contracts.Agents;
using Agentic.Contracts.Runs;
using Agentic.Contracts.Tools;
using Agentic.Contracts.Scripting;
using Agentic.Data;
using Agentic.Domains.AgentRuns.Models;
using Agentic.Domains.AgentRuns.Services;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>
/// Delegation on PostgreSQL (ADR-0033): an agent hands a task to another agent with <c>agent.delegate</c>. The
/// delegated run acts for the same person, never with more autonomy, inherits the taint, waits for its own approvals,
/// and its outcome comes back as the call's result; cancelling and erasing cascade; nothing starts twice.
/// </summary>
[Collection(AgenticPostgresCollection.Name)]
public class AgentDelegationTests
{
    private readonly AgenticPostgresFixture _fx;

    public AgentDelegationTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
    }

    private static ScriptedReply Delegate(string agentKey, string task) =>
        ScriptedReply.Call(AgentToolNames.Delegate, JsonSerializer.Serialize(new { agentKey, task }));

    private Task<List<AgentRun>> ChildrenOfAsync(TenantInfo tenant, Guid parentId) =>
        _fx.InTenantAsync(tenant, sp => sp.GetRequiredService<AgenticDbContext>().Runs.AsNoTracking()
            .Where(r => r.ParentRunId == parentId).ToListAsync());

    private async Task<AgentRunStep> DelegateStepAsync(TenantInfo tenant, Guid runId) =>
        (await _fx.StepsOfAsync(tenant, runId)).Single(s => s.ToolName == AgentToolNames.Delegate);

    /// <summary>The delegator delegates on its first turn and answers with what it got; the worker follows <paramref name="worker"/>.</summary>
    private void Script(Func<ScriptedTurn, ScriptedReply> worker, string target = TestAgents.Worker, ScriptedReply? first = null) =>
        _fx.Board.Next = turn => turn.AgentKey switch
        {
            TestAgents.Delegator when first is not null && turn.TurnNumber == 1 => first,
            TestAgents.Delegator when turn.ToolResults.All(r => r.Tool != AgentToolNames.Delegate) =>
                Delegate(target, "Look up item A-1 and tell me its colour."),
            TestAgents.Delegator => ScriptedReply.Say("The worker said: " + turn.ToolResults.Last(r => r.Tool == AgentToolNames.Delegate).ResultJson),
            _ => worker(turn)
        };

    [Fact]
    public async Task ADelegatedTask_RunsAsTheSamePerson_AndItsAnswerComesBackToTheAgentThatDelegated()
    {
        Script(_ => ScriptedReply.Say("A-1 is red."));
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "What colour is A-1?");

        await _fx.RunAsync(TenantA, parent.Id);

        (await _fx.RunOfAsync(TenantA, parent.Id)).Status.Should().Be(AgentRunStatus.AwaitingDelegate);
        (await DelegateStepAsync(TenantA, parent.Id)).Status.Should().Be(AgentRunStepStatus.AwaitingDelegate);
        var child = (await ChildrenOfAsync(TenantA, parent.Id)).Should().ContainSingle().Subject;
        child.TriggerKind.Should().Be(AgentTriggerKind.Delegated);
        child.InitiatedBy.Should().Be(TestCaller.Alice);
        child.Depth.Should().Be(1);
        child.Autonomy.Should().Be(AgentAutonomy.Prepare, "never more than the delegating run (Prepare), whatever the worker's own");
        child.Prompt.Should().Be("Look up item A-1 and tell me its colour.");

        await _fx.RunAsync(TenantA, child.Id);
        (await _fx.RunOfAsync(TenantA, parent.Id)).Status.Should().Be(AgentRunStatus.Queued, "the finished delegate resumed it");
        await _fx.RunAsync(TenantA, parent.Id);

        var finished = await _fx.RunOfAsync(TenantA, parent.Id);
        finished.Status.Should().Be(AgentRunStatus.Completed);
        finished.OutputText.Should().Contain("A-1 is red.").And.Contain(child.Id.ToString());
        var doneChild = await _fx.RunOfAsync(TenantA, child.Id);
        finished.InputTokens.Should().BeGreaterThan(doneChild.InputTokens, "the delegate's tokens count against the parent too");
        (await DelegateStepAsync(TenantA, parent.Id)).Status.Should().Be(AgentRunStepStatus.Succeeded);
        _fx.Jobs.For(parent.Id).Should().HaveCountGreaterThanOrEqualTo(2, "started, then queued again after the delegate");
    }

    [Fact]
    public async Task ADelegatedRunThatNeedsApproval_WaitsForAPerson_WhileTheParentWaitsForIt()
    {
        // The parent read untrusted content, so the delegate's change needs a person.
        Script(turn => turn.TurnNumber == 1
                ? ScriptedReply.Call(CreateNoteTool.Name, """{"title":"Invoice note"}""")
                : ScriptedReply.Say("Note written."),
            first: ScriptedReply.Call(ReadDocumentTool.Name, """{"code":"D-1"}"""));
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "Write the invoice note");
        await _fx.RunAsync(TenantA, parent.Id);
        var child = (await ChildrenOfAsync(TenantA, parent.Id)).Single();

        await _fx.RunAsync(TenantA, child.Id);
        (await _fx.RunOfAsync(TenantA, child.Id)).Status.Should().Be(AgentRunStatus.AwaitingApproval);
        (await _fx.RunOfAsync(TenantA, parent.Id)).Status.Should().Be(AgentRunStatus.AwaitingDelegate);

        await _fx.ApproveAndRunAsync(TenantA, child.Id, TestCaller.Bob);
        await _fx.RunAsync(TenantA, parent.Id);

        (await _fx.RunOfAsync(TenantA, child.Id)).Status.Should().Be(AgentRunStatus.Completed);
        (await _fx.RunOfAsync(TenantA, parent.Id)).Status.Should().Be(AgentRunStatus.Completed);
        _fx.World.Executions(CreateNoteTool.Name).Should().Be(1);
    }

    [Fact]
    public async Task ADelegatedRun_CanNotDoWhatTheDelegatingRunsAutonomyForbids()
    {
        Script(turn => turn.TurnNumber == 1
            ? ScriptedReply.Call(SendMailTool.Name, """{"title":"Invoice"}""")
            : ScriptedReply.Say("Could not send."));
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "Send the invoice");
        await _fx.RunAsync(TenantA, parent.Id);
        var child = (await ChildrenOfAsync(TenantA, parent.Id)).Single();

        await _fx.RunAsync(TenantA, child.Id);

        (await _fx.StepsOfAsync(TenantA, child.Id)).Single(s => s.ToolName == SendMailTool.Name).ErrorCode
            .Should().Be("autonomy_exceeded", "the worker may send mail on its own, but not for a Prepare delegator");
        _fx.World.Executions(SendMailTool.Name).Should().Be(0);
    }

    [Fact]
    public async Task ADelegationBeyondTheConfiguredDepth_IsRefused_AndTheModelIsTold()
    {
        _fx.Options.Delegation.MaxDepth = 1;
        Script(turn => turn.AgentKey == TestAgents.Worker && turn.TurnNumber == 1
            ? Delegate(TestAgents.Leaf, "Look up A-1")
            : ScriptedReply.Say("Could not delegate."));
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "Go");
        await _fx.RunAsync(TenantA, parent.Id);
        var child = (await ChildrenOfAsync(TenantA, parent.Id)).Single();

        await _fx.RunAsync(TenantA, child.Id);

        (await DelegateStepAsync(TenantA, child.Id)).ErrorCode.Should().Be("delegation_too_deep");
        (await ChildrenOfAsync(TenantA, child.Id)).Should().BeEmpty();
        (await _fx.RunOfAsync(TenantA, child.Id)).Status.Should().Be(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task DelegatingToAnAgentThePersonMayNotStart_IsRefused_AndStartsNothing()
    {
        _fx.Caller.Denied.Add(TenantPermissions.WebhooksManage);
        Script(_ => ScriptedReply.Say("never"), target: TestAgents.Restricted);
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "Go");

        await _fx.RunAsync(TenantA, parent.Id);

        (await DelegateStepAsync(TenantA, parent.Id)).ErrorCode.Should().Be("forbidden");
        (await ChildrenOfAsync(TenantA, parent.Id)).Should().BeEmpty();
        (await _fx.RunOfAsync(TenantA, parent.Id)).Status.Should().Be(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task AnAgentNotInTheDelegates_IsRefusedLikeAnUnknownTool()
    {
        Script(_ => ScriptedReply.Say("never"), target: TestAgents.Leaf);
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "Go");

        await _fx.RunAsync(TenantA, parent.Id);

        (await DelegateStepAsync(TenantA, parent.Id)).ErrorCode.Should().Be("tool_not_allowed");
        (await ChildrenOfAsync(TenantA, parent.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task CancellingTheParent_CancelsTheDelegatedRun()
    {
        Script(_ => ScriptedReply.Say("never"));
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "Go");
        await _fx.RunAsync(TenantA, parent.Id);
        var child = (await ChildrenOfAsync(TenantA, parent.Id)).Single();

        await _fx.SendAsync(TenantA, new CancelAgentRunCommand(parent.Id));

        (await _fx.RunOfAsync(TenantA, parent.Id)).Status.Should().Be(AgentRunStatus.Cancelled);
        (await _fx.RunOfAsync(TenantA, child.Id)).Status.Should().Be(AgentRunStatus.Cancelled);
    }

    [Fact]
    public async Task ARetriedAttempt_FindsTheDelegatedRun_InsteadOfStartingASecondOne()
    {
        Script(_ => ScriptedReply.Say("A-1 is red."));
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "Go");
        await _fx.RunAsync(TenantA, parent.Id);
        var step = await DelegateStepAsync(TenantA, parent.Id);
        // A worker that stopped after starting the delegate and before saving the suspension.
        await _fx.AdminExecAsync($"update agentic.agent_runs set status = 'Queued' where id = '{parent.Id}'; " +
                                 $"update agentic.agent_run_steps set status = 'Pending' where id = '{step.Id}'");

        await _fx.RunAsync(TenantA, parent.Id);
        await _fx.RunAsync(TenantA, parent.Id); // a duplicate delivery while it waits does nothing

        (await ChildrenOfAsync(TenantA, parent.Id)).Should().ContainSingle();
        (await _fx.RunOfAsync(TenantA, parent.Id)).Status.Should().Be(AgentRunStatus.AwaitingDelegate);
    }

    [Fact]
    public async Task ATaintedParent_StartsATaintedDelegate_WhoseChangesNeedAPerson_AndATaintedDelegateTaintsTheParent()
    {
        Script(turn => turn.TurnNumber == 1
                ? ScriptedReply.Call(CreateNoteTool.Name, """{"title":"from the document"}""")
                : ScriptedReply.Say("Waiting."),
            first: ScriptedReply.Call(ReadDocumentTool.Name, """{"code":"D-1"}"""));
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "Read D-1 and act on it");
        await _fx.RunAsync(TenantA, parent.Id);

        var child = (await ChildrenOfAsync(TenantA, parent.Id)).Single();
        child.Tainted.Should().BeTrue("the parent read untrusted content before it delegated");
        await _fx.RunAsync(TenantA, child.Id);
        (await _fx.RunOfAsync(TenantA, child.Id)).Status.Should().Be(AgentRunStatus.AwaitingApproval,
            "a low-risk change of a tainted run needs a person");
    }

    [Fact]
    public async Task ADelegateThatReadUntrustedContent_TaintsTheParent_AndAFailedDelegateIsAFailedCall()
    {
        _fx.Options.Limits.MaxModelTurns = 2;
        Script(turn => ScriptedReply.Call(ReadDocumentTool.Name, """{"code":"D-1"}""")); // never answers: runs out of turns
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "Go");
        await _fx.RunAsync(TenantA, parent.Id);
        var child = (await ChildrenOfAsync(TenantA, parent.Id)).Single();

        await _fx.RunAsync(TenantA, child.Id);
        await _fx.RunAsync(TenantA, parent.Id);

        (await _fx.RunOfAsync(TenantA, child.Id)).Status.Should().Be(AgentRunStatus.Failed);
        var step = await DelegateStepAsync(TenantA, parent.Id);
        step.ErrorCode.Should().Be("delegate_failed");
        step.ResultJson.Should().Contain("budget_exceeded");
        var after = await _fx.RunOfAsync(TenantA, parent.Id);
        after.Tainted.Should().BeTrue("the delegate's answer may carry what it read");
        after.Status.Should().Be(AgentRunStatus.Completed);
    }

    [Fact]
    public async Task ErasingTheParentsContent_ErasesTheDelegatedRunsContentToo()
    {
        Script(_ => ScriptedReply.Say("A-1 is red."));
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "Go");
        await _fx.RunAsync(TenantA, parent.Id);
        var child = (await ChildrenOfAsync(TenantA, parent.Id)).Single();
        await _fx.RunAsync(TenantA, child.Id);
        await _fx.RunAsync(TenantA, parent.Id);

        await _fx.InTenantAsync(TenantA, sp => AgentRunContentEraser.EraseAsync(sp.GetRequiredService<AgenticDbContext>(),
            [parent.Id], DateTime.UtcNow, CancellationToken.None));

        var erased = await _fx.RunOfAsync(TenantA, child.Id);
        erased.ContentErasedAt.Should().NotBeNull();
        erased.Prompt.Should().BeNull();
        erased.OutputText.Should().BeNull();
    }

    [Fact]
    public async Task AnotherTenant_CannotSeeTheDelegatedRun()
    {
        Script(_ => ScriptedReply.Say("A-1 is red."));
        var parent = await _fx.StartAsync(TenantA, TestAgents.Delegator, "Go");
        await _fx.RunAsync(TenantA, parent.Id);
        var child = (await ChildrenOfAsync(TenantA, parent.Id)).Single();

        var read = () => _fx.SendAsync(TenantB, new GetAgentRunByIdQuery(child.Id));

        await read.Should().ThrowAsync<Shared.Core.Exceptions.NotFoundException>();
        (await _fx.SendAsync(TenantA, new GetAgentRunByIdQuery(child.Id))).Run.ParentRunId.Should().Be(parent.Id);
    }
}
