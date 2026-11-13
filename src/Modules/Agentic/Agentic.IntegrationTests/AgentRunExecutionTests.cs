using System.Text.Json;
using Agentic.Contracts.Agents;
using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Domains.AgentRuns.Exceptions;
using Agentic.Domains.AgentRuns.Models;
using Shared.Core.MultiTenancy.Authorization;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>A run from the request that starts it to its final state, on real Postgres through the real module.</summary>
[Collection(AgenticPostgresCollection.Name)]
public class AgentRunExecutionTests
{
    private readonly AgenticPostgresFixture _fx;

    public AgentRunExecutionTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
    }

    [Fact]
    public async Task Start_OnlyQueuesTheRun_AndNothingRunsInsideTheRequest()
    {
        var run = await _fx.StartAsync(TenantA, prompt: "Find item A-1");

        run.Status.Should().Be(AgentRunStatus.Queued);
        run.InitiatedBy.Should().Be(TestCaller.Alice);
        run.TriggerKind.Should().Be(AgentTriggerKind.User);
        _fx.Board.Turns.Should().Be(0, "the model is called by the job, never by the request");
        _fx.Jobs.For(run.Id).Should().ContainSingle().Which.IsScheduled.Should().BeFalse();
        (await _fx.StepsOfAsync(TenantA, run.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task ARun_CallsTheTool_AsItsInitiator_InItsTenant_AndCompletesWithTheModelsAnswer()
    {
        _fx.Board.Play(
            ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}"""),
            ScriptedReply.Say("The item is Item A-1."));
        var started = await _fx.StartAsync(TenantA, prompt: "Find item A-1");

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Completed);
        run.OutputText.Should().Be("The item is Item A-1.");
        run.ModelTurns.Should().Be(2);
        run.ToolCalls.Should().Be(1);
        run.TotalTokens.Should().BeGreaterThan(0);
        run.LeaseOwner.Should().BeNull();
        run.FinishedAt.Should().NotBeNull();

        var steps = await _fx.StepsOfAsync(TenantA, started.Id);
        steps.Select(s => (s.Sequence, s.Kind, s.Status)).Should().Equal(
            (1, AgentRunStepKind.ModelTurn, AgentRunStepStatus.Succeeded),
            (2, AgentRunStepKind.ToolCall, AgentRunStepStatus.Succeeded),
            (3, AgentRunStepKind.ModelTurn, AgentRunStepStatus.Succeeded));
        steps[1].ToolName.Should().Be(GetItemTool.Name);
        steps[1].ToolVersion.Should().Be(1);
        steps[1].Executions.Should().Be(1);
        JsonDocument.Parse(steps[1].ResultJson!).RootElement.GetProperty("name").GetString().Should().Be("Item A-1");

        // The tool saw exactly what a handler of Alice's own request would see.
        var seen = _fx.World.Observations.Should().ContainSingle().Subject;
        seen.TenantId.Should().Be(TenantA.Id);
        seen.UserId.Should().Be(TestCaller.Alice);
        seen.CorrelationId.Should().Be(AgentRun.CorrelationIdOf(started.Id));
        seen.IdempotencyKey.Should().Be($"{started.Id:N}:{steps[1].Id:N}");
    }

    [Fact]
    public async Task TheToolRuns_InTheInitiatorsCurrentBranchScope()
    {
        var branch = Guid.NewGuid();
        _fx.Caller.AllowedBranches = [branch];
        _fx.Board.Play(ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}"""));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        _fx.World.Observations.Should().ContainSingle().Which.Branches.AllowedBranchIds.Should().Equal(branch);
    }

    [Fact]
    public async Task ATypedAgent_CompletesOnlyWhenAValidResultIsHandedIn()
    {
        _fx.Board.Play(
            ScriptedReply.Say("The summary is: two items."), // prose instead of the result: reminded
            ScriptedReply.Submit("""{"title":"Items","items":2,"approved":true}"""), // a member the type does not declare
            ScriptedReply.Submit("""{"title":"Items","items":2}"""));
        var started = await _fx.StartAsync(TenantA, TestAgents.Typed);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Completed);
        var output = JsonDocument.Parse(run.OutputJson!).RootElement;
        output.GetProperty("title").GetString().Should().Be("Items");
        output.GetProperty("items").GetInt32().Should().Be(2);
        output.TryGetProperty("approved", out _).Should().BeFalse();
        run.ModelTurns.Should().Be(3);
        run.ToolCalls.Should().Be(0, "handing in the result is not a business tool call");

        var submits = (await _fx.StepsOfAsync(TenantA, started.Id)).Where(s => s.Kind == AgentRunStepKind.ToolCall).ToList();
        submits.Select(s => (s.Status, s.ErrorCode)).Should().Equal(
            (AgentRunStepStatus.Failed, "invalid_result"),
            (AgentRunStepStatus.Succeeded, null));
    }

    [Fact]
    public async Task ABusinessError_FromATool_IsReportedToTheModel_AndDoesNotFailTheRun()
    {
        _fx.Board.Play(
            ScriptedReply.Call(GetItemTool.Name, """{"code":"missing"}"""),
            ScriptedReply.Say("There is no such item."));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Completed);
        var call = (await _fx.StepsOfAsync(TenantA, started.Id)).Single(s => s.Kind == AgentRunStepKind.ToolCall);
        call.Status.Should().Be(AgentRunStepStatus.Failed);
        call.ErrorCode.Should().Be("not_found");
        JsonDocument.Parse(call.ResultJson!).RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("not_found");
    }

    [Fact]
    public async Task AReadOnlyTool_ThatTakesTooLong_TimesOut_AndTheRunGoesOn()
    {
        _fx.Board.Play(
            ScriptedReply.Call(SlowLookupTool.Name, """{"code":"A-1"}"""),
            ScriptedReply.Say("The lookup did not answer."));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Completed);
        (await _fx.StepsOfAsync(TenantA, started.Id)).Single(s => s.Kind == AgentRunStepKind.ToolCall)
            .ErrorCode.Should().Be("timeout");
    }

    [Fact]
    public async Task AModelThatNeverStops_IsStoppedByTheTurnBudget()
    {
        _fx.Options.Limits.MaxModelTurns = 3;
        _fx.Board.Next = _ => ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}""");
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureCode.Should().Be("budget_exceeded");
        run.FailureMessage.Should().Contain("max_model_turns");
        run.ModelTurns.Should().Be(3);
        _fx.Board.Turns.Should().Be(3, "no model request is made beyond the budget");
    }

    [Fact]
    public async Task ToolCalls_BeyondTheBudget_StopTheRun_BeforeTheNextToolRuns()
    {
        _fx.Options.Limits.MaxToolCalls = 2;
        _fx.Board.Next = _ => ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}""");
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureMessage.Should().Contain("max_tool_calls");
        _fx.World.Executions(GetItemTool.Name).Should().Be(2);
    }

    [Fact]
    public async Task TheTokenBudget_StopsTheRun()
    {
        _fx.Options.Limits.MaxTokens = 1;
        _fx.Board.Next = _ => ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}""");
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureMessage.Should().Contain("max_tokens");
        run.ModelTurns.Should().Be(1);
    }

    [Fact]
    public async Task OnlyTheFirstCallsOfATurn_UpToTheLimit_AreTaken()
    {
        _fx.Options.Limits.MaxToolCallsPerTurn = 2;
        _fx.Board.Play(new ScriptedReply(null,
        [
            new ScriptedToolCall(GetItemTool.Name, """{"code":"A-1"}"""),
            new ScriptedToolCall(GetItemTool.Name, """{"code":"A-2"}"""),
            new ScriptedToolCall(GetItemTool.Name, """{"code":"A-3"}""")
        ]));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        (await _fx.RunOfAsync(TenantA, started.Id)).Status.Should().Be(AgentRunStatus.Completed);
        _fx.World.Executions(GetItemTool.Name).Should().Be(2);
    }

    [Fact]
    public async Task Start_OfAnUnknownAgent_IsNotFound()
    {
        var act = () => _fx.StartAsync(TenantA, "test.nobody");

        await act.Should().ThrowAsync<AgentNotFoundException>();
    }

    [Fact]
    public async Task Start_WithoutPromptOrInput_FailsValidation()
    {
        var act = () => _fx.SendAsync(TenantA, new StartAgentRunCommand(TestAgents.Assistant));

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    [Fact]
    public async Task Start_WithAnInputThatIsNotAJsonObject_FailsValidation()
    {
        var act = () => _fx.SendAsync(TenantA, new StartAgentRunCommand(TestAgents.Assistant, "x", "[1,2]"));

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    [Fact]
    public async Task Start_WhileTheModuleIsSwitchedOff_IsNotFound_AndAQueuedRunIsNotExecuted()
    {
        var queued = await _fx.StartAsync(TenantA);
        _fx.Options.Enabled = false;

        var start = () => _fx.StartAsync(TenantA);
        await start.Should().ThrowAsync<AgenticDisabledException>();

        await _fx.RunAsync(TenantA, queued.Id);
        var run = await _fx.RunOfAsync(TenantA, queued.Id);
        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureCode.Should().Be("agents_disabled");
        _fx.Board.Turns.Should().Be(0);
    }

    [Fact]
    public async Task AnAgent_TheCallerMayNotStart_DoesNotExistForThem()
    {
        _fx.Caller.Denied.Add(TenantPermissions.WebhooksManage);

        var start = () => _fx.StartAsync(TenantA, TestAgents.Restricted);
        await start.Should().ThrowAsync<AgentNotFoundException>();

        var agents = await _fx.SendAsync(TenantA, new GetAgentsQuery());
        agents.Select(a => a.Key).Should().Contain(TestAgents.Assistant).And.NotContain(TestAgents.Restricted);
    }

    [Fact]
    public async Task GetAgentByKey_ListsTheTools_WithTheirRiskAndPermission()
    {
        var detail = await _fx.SendAsync(TenantA, new GetAgentByKeyQuery(TestAgents.Assistant));

        detail.Agent.Autonomy.Should().Be(AgentAutonomy.Execute);
        var mail = detail.Tools.Single(t => t.Name == SendMailTool.Name);
        mail.Risk.Should().Be(Agentic.Contracts.Tools.AgentToolRisk.ExternalOrIrreversible);
        mail.Permission.Should().Be(TenantPermissions.WebhooksManage);
    }
}
