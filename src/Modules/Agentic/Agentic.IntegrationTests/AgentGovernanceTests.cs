using Agentic.Contracts.Agents;
using Agentic.Contracts.Policies;
using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Contracts.Usage;
using Agentic.Domains.AgentRuns.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Entitlements;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy.Authorization;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>
/// What bounds agents beyond a single tool call: automatic triggers (explicit, once per trigger, rate-limited, always
/// for a real user), tenant policies (which only ever tighten), plan quotas, capacity and the usage report.
/// </summary>
[Collection(AgenticPostgresCollection.Name)]
public class AgentGovernanceTests : IAsyncLifetime
{
    private readonly AgenticPostgresFixture _fx;

    public AgentGovernanceTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
    }

    /// <summary>The tests share one database: start from "nothing is in progress and no policy is set".</summary>
    public async ValueTask InitializeAsync()
    {
        await _fx.AdminExecAsync(
            "update agentic.agent_runs set status = 'Cancelled' where status in ('Queued', 'Running', 'AwaitingApproval', 'AwaitingDelegate'); " +
            "update agentic.agent_runs set created_at = now() - interval '2 hours' where trigger_kind in ('Event', 'Schedule'); " +
            "delete from agentic.agent_policies;");
    }

    public ValueTask DisposeAsync() => new(_fx.AdminExecAsync("delete from agentic.agent_policies;"));

    private static RequestAgentRunCommand Trigger(string key, Guid? user = null, string agent = TestAgents.Automatic,
        AgentTriggerKind kind = AgentTriggerKind.Event) =>
        new(agent, kind, key, user ?? TestCaller.Bob, "A document was uploaded");

    private static string NewKey() => $"document:{Guid.NewGuid():N}:uploaded";

    // ----- automatic triggers -------------------------------------------------------------------------------------

    [Fact]
    public async Task AnEvent_StartsARun_ForTheUserItActsFor_AndTheToolsRunAsThatUser()
    {
        _fx.Board.Play(ScriptedReply.Call(CreateNoteTool.Name, """{"title":"from an event"}"""));
        var key = NewKey();

        var result = await _fx.SendWithoutRequestAsync(TenantA, Trigger(key, TestCaller.Bob));

        result.Created.Should().BeTrue();
        var run = await _fx.RunOfAsync(TenantA, result.RunId);
        run.TriggerKind.Should().Be(AgentTriggerKind.Event);
        run.TriggerKey.Should().Be(key);
        run.InitiatedBy.Should().Be(TestCaller.Bob);
        run.Status.Should().Be(AgentRunStatus.Queued);
        _fx.Jobs.For(run.Id).Should().ContainSingle();

        await _fx.RunAsync(TenantA, run.Id);

        (await _fx.RunOfAsync(TenantA, run.Id)).Status.Should().Be(AgentRunStatus.Completed);
        _fx.World.Observations.Should().ContainSingle().Which.UserId.Should().Be(TestCaller.Bob);
    }

    [Fact]
    public async Task TheSameTrigger_DeliveredAgain_ReturnsTheSameRun_AndStartsNothingNew()
    {
        var key = NewKey();

        var first = await _fx.SendWithoutRequestAsync(TenantA, Trigger(key));
        var second = await _fx.SendWithoutRequestAsync(TenantA, Trigger(key));
        var third = await _fx.SendWithoutRequestAsync(TenantA, Trigger(key));

        second.Should().Be(new RequestAgentRunResult(first.RunId, Created: false));
        third.RunId.Should().Be(first.RunId);
        _fx.Jobs.For(first.RunId).Should().ContainSingle();
        (await _fx.AdminScalarAsync<long>($"select count(*) from agentic.agent_runs where trigger_key = '{key}'")).Should().Be(1);
        _fx.Quota.Used(EntitlementKeys.AgentRuns).Should().Be(1, "a duplicate trigger costs nothing");
    }

    [Fact]
    public async Task ATrigger_ThatLosesTheRaceForItsKey_ReturnsTheWinner_AndTheSameScopeCanStartAnotherRun()
    {
        var key = NewKey();
        var first = await _fx.SendWithoutRequestAsync(TenantA, Trigger(key));

        var (duplicate, another) = await _fx.InTenantAsync(TenantA, async sp =>
        {
            var starter = sp.GetRequiredService<Domains.AgentRuns.Services.AgentRunStarter>();
            var registry = sp.GetRequiredService<Runtime.Tools.AgentRegistry>();
            var sender = sp.GetRequiredService<Shared.Contracts.Mediation.ISender>();

            // The insert fails on the unique trigger index, exactly as for the delivery that lost a race.
            var lost = async () => await starter.StartAsync(new Domains.AgentRuns.Services.AgentRunStart(
                registry.GetAgent(TestAgents.Automatic), AgentTriggerKind.Event, TestCaller.Bob, "x", null, "en",
                TriggerKey: key), CancellationToken.None);
            await lost.Should().ThrowAsync<DbUpdateException>();

            // The same unit of work goes on: the failed run must not be inserted again with the next one.
            return (await sender.Send(Trigger(key)), await sender.Send(Trigger(NewKey())));
        });

        duplicate.Should().Be(new RequestAgentRunResult(first.RunId, Created: false));
        another.Created.Should().BeTrue();
        (await _fx.AdminScalarAsync<long>($"select count(*) from agentic.agent_runs where trigger_key = '{key}'")).Should().Be(1);
    }

    [Fact]
    public async Task TheSameTriggerKey_InAnotherTenant_IsADifferentRun()
    {
        var key = NewKey();

        var inA = await _fx.SendWithoutRequestAsync(TenantA, Trigger(key));
        var inB = await _fx.SendWithoutRequestAsync(TenantB, Trigger(key));

        inB.Created.Should().BeTrue();
        inB.RunId.Should().NotBe(inA.RunId);
    }

    [Fact]
    public async Task AnAgent_ThatDoesNotAllowEvents_CannotBeStartedByOne()
    {
        var act = () => _fx.SendWithoutRequestAsync(TenantA, Trigger(NewKey(), agent: TestAgents.Assistant));

        (await act.Should().ThrowAsync<AgentAccessDeniedException>()).Which.ErrorCode.Should().Be("trigger_not_allowed");
    }

    [Fact]
    public async Task AnAgent_ThatAllowsOnlyAutomaticTriggers_CannotBeStartedByAUser()
    {
        var act = () => _fx.StartAsync(TenantA, TestAgents.Automatic);

        (await act.Should().ThrowAsync<AgentAccessDeniedException>()).Which.ErrorCode.Should().Be("trigger_not_allowed");
    }

    [Fact]
    public async Task AUserKindOfTrigger_IsNotAcceptedThroughTheAutomaticPath()
    {
        var act = () => _fx.SendWithoutRequestAsync(TenantA, Trigger(NewKey(), kind: AgentTriggerKind.User));

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    [Theory]
    [InlineData("not-a-member")]
    [InlineData("inactive")]
    [InlineData("no-start-permission")]
    public async Task AnAutomaticRun_NeedsARealUser_WhoMayStartTheAgentRightNow(string reason)
    {
        switch (reason)
        {
            case "not-a-member":
                _fx.Caller.NotMembers.Add(TestCaller.Bob);
                break;
            case "inactive":
                _fx.Caller.Inactive.Add(TestCaller.Bob);
                break;
            default:
                _fx.Caller.DeniedTo.Add((TestCaller.Bob, TenantPermissions.AgentRunsStart));
                break;
        }

        var key = NewKey();
        var act = () => _fx.SendWithoutRequestAsync(TenantA, Trigger(key, TestCaller.Bob));

        (await act.Should().ThrowAsync<AgentAccessDeniedException>()).Which.ErrorCode.Should().Be("on_behalf_of_not_authorized");
        (await _fx.AdminScalarAsync<long>($"select count(*) from agentic.agent_runs where trigger_key = '{key}'")).Should().Be(0);
    }

    [Fact]
    public async Task AutomaticRuns_OfOneAgent_AreLimitedPerHour_SoAnEventLoopCannotRunAway()
    {
        _fx.Options.Triggers.MaxAutomaticRunsPerHour = 2;
        await _fx.SendWithoutRequestAsync(TenantA, Trigger(NewKey()));
        await _fx.SendWithoutRequestAsync(TenantA, Trigger(NewKey()));

        var third = () => _fx.SendWithoutRequestAsync(TenantA, Trigger(NewKey()));

        var refused = (await third.Should().ThrowAsync<AgentRunLimitException>()).Which;
        refused.ErrorCode.Should().Be("automatic_run_limit");
        refused.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
        (await _fx.SendWithoutRequestAsync(TenantB, Trigger(NewKey()))).Created.Should().BeTrue("the limit is per tenant");
    }

    // ----- tenant policies ----------------------------------------------------------------------------------------

    [Fact]
    public async Task WithoutAPolicy_TheDefaultsApply_AndAreReportedAsSuch()
    {
        var policy = await _fx.SendAsync(TenantA, new GetAgentPolicyQuery(TestAgents.Assistant));

        policy.Should().BeEquivalentTo(new AgentPolicyDto(TestAgents.Assistant, true, null, true, false, null, null, null,
            IsDefault: true, Version: 0));
    }

    [Fact]
    public async Task AnAgent_SwitchedOffForATenant_CannotBeStartedThere_IsNotOffered_AndStillWorksElsewhere()
    {
        await _fx.SendAsync(TenantA, new UpdateAgentPolicyCommand(TestAgents.Assistant, Enabled: false));

        var start = () => _fx.StartAsync(TenantA);
        (await start.Should().ThrowAsync<AgentAccessDeniedException>()).Which.ErrorCode.Should().Be("agent_disabled");
        (await _fx.SendAsync(TenantA, new GetAgentsQuery())).Select(a => a.Key).Should().NotContain(TestAgents.Assistant);

        (await _fx.StartAsync(TenantB)).Status.Should().Be(AgentRunStatus.Queued);
        (await _fx.SendAsync(TenantB, new GetAgentsQuery())).Select(a => a.Key).Should().Contain(TestAgents.Assistant);
    }

    [Fact]
    public async Task AnAutonomyCeiling_LowersWhatTheAgentMayDo_InThatTenant()
    {
        await _fx.SendAsync(TenantA, new UpdateAgentPolicyCommand(TestAgents.Assistant, AutonomyCeiling: AgentAutonomy.Recommend));
        _fx.Board.Play(ScriptedReply.Call(CreateNoteTool.Name, """{"title":"not allowed here"}"""));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        (await _fx.RunOfAsync(TenantA, started.Id)).Autonomy.Should().Be(AgentAutonomy.Recommend);
        (await _fx.StepsOfAsync(TenantA, started.Id)).Single(s => s.Kind == AgentRunStepKind.ToolCall)
            .ErrorCode.Should().Be("autonomy_exceeded");
        _fx.World.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task ACeiling_AboveTheDefinition_ChangesNothing_APolicyNeverWidens()
    {
        await _fx.SendAsync(TenantA, new UpdateAgentPolicyCommand(TestAgents.Typed, AutonomyCeiling: AgentAutonomy.Execute,
            MaxModelTurns: 10_000, MaxToolCalls: 10_000, MaxTokens: long.MaxValue));

        var started = await _fx.StartAsync(TenantA, TestAgents.Typed);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Autonomy.Should().Be(AgentAutonomy.Prepare, "the definition's autonomy is the upper bound");
        run.MaxModelTurns.Should().Be(_fx.Options.Limits.MaxModelTurns);
        run.MaxToolCalls.Should().Be(_fx.Options.Limits.MaxToolCalls);
        run.MaxTokens.Should().Be(_fx.Options.Limits.MaxTokens);
    }

    [Fact]
    public async Task APolicy_CanDemandAPersonForEveryChange_WhateverItsRisk()
    {
        await _fx.SendAsync(TenantA, new UpdateAgentPolicyCommand(TestAgents.Assistant, RequireApprovalForChanges: true));
        _fx.Board.Play(
            ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}"""),
            ScriptedReply.Call(CreateNoteTool.Name, """{"title":"low risk, still reviewed"}"""));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.AwaitingApproval);
        run.ApproveAllChanges.Should().BeTrue();
        _fx.World.Executions(GetItemTool.Name).Should().Be(1, "reading needs no approval");
        _fx.World.Notes.Should().BeEmpty();

        await _fx.ApproveAndRunAsync(TenantA, started.Id, TestCaller.Bob);
        _fx.World.Notes.Values.Should().Equal("low risk, still reviewed");
    }

    [Fact]
    public async Task APolicy_CanLowerTheBudgets_AndARunInProgressKeepsTheOnesItStartedWith()
    {
        var before = await _fx.StartAsync(TenantA);
        await _fx.SendAsync(TenantA, new UpdateAgentPolicyCommand(TestAgents.Assistant, MaxModelTurns: 1, MaxToolCalls: 0));
        var after = await _fx.StartAsync(TenantA);

        (await _fx.RunOfAsync(TenantA, before.Id)).MaxModelTurns.Should().Be(_fx.Options.Limits.MaxModelTurns);
        var limited = await _fx.RunOfAsync(TenantA, after.Id);
        limited.MaxModelTurns.Should().Be(1);
        limited.MaxToolCalls.Should().Be(0);

        _fx.Board.Next = _ => ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}""");
        await _fx.RunAsync(TenantA, after.Id);
        var run = await _fx.RunOfAsync(TenantA, after.Id);
        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureCode.Should().Be("budget_exceeded");
        _fx.World.Executions(GetItemTool.Name).Should().Be(0);
    }

    [Fact]
    public async Task APolicy_CanRefuseAutomaticTriggers()
    {
        await _fx.SendAsync(TenantA, new UpdateAgentPolicyCommand(TestAgents.Automatic, AllowAutomaticTriggers: false));

        var act = () => _fx.SendWithoutRequestAsync(TenantA, Trigger(NewKey()));

        (await act.Should().ThrowAsync<AgentAccessDeniedException>()).Which.ErrorCode.Should().Be("automatic_triggers_disabled");
    }

    [Fact]
    public async Task UpdatePolicy_WithStaleVersion_IsRejected_AndNothingChanges()
    {
        var created = await _fx.SendAsync(TenantA, new UpdateAgentPolicyCommand(TestAgents.Assistant, MaxModelTurns: 5));
        created.IsDefault.Should().BeFalse();

        var stale = () => _fx.SendAsync(TenantA,
            new UpdateAgentPolicyCommand(TestAgents.Assistant, Enabled: false, Version: created.Version + 3));
        await stale.Should().ThrowAsync<ConcurrencyConflictException>();

        var current = await _fx.SendAsync(TenantA, new GetAgentPolicyQuery(TestAgents.Assistant));
        current.Enabled.Should().BeTrue();
        current.MaxModelTurns.Should().Be(5);

        var updated = await _fx.SendAsync(TenantA,
            new UpdateAgentPolicyCommand(TestAgents.Assistant, Enabled: false, Version: current.Version));
        updated.Enabled.Should().BeFalse();
        updated.Version.Should().BeGreaterThan(current.Version);
    }

    [Fact]
    public async Task APolicy_OfAnUnknownAgent_IsNotFound_AndInvalidLimitsFailValidation()
    {
        var unknown = () => _fx.SendAsync(TenantA, new UpdateAgentPolicyCommand("test.nobody"));
        var invalid = () => _fx.SendAsync(TenantA, new UpdateAgentPolicyCommand(TestAgents.Assistant, MaxModelTurns: 0));

        await unknown.Should().ThrowAsync<AgentNotFoundException>();
        await invalid.Should().ThrowAsync<FluentValidation.ValidationException>();
    }

    [Fact]
    public async Task APolicyChange_IsWrittenToTheAuditTrail_AndBelongsToItsTenantOnly()
    {
        await _fx.SendAsync(TenantA, new UpdateAgentPolicyCommand(TestAgents.Assistant, Enabled: false), TestCaller.Bob);

        (await _fx.AdminScalarAsync<long>(
            $"select count(*) from agentic.audit_entries where entity_type = 'AgentPolicy' and tenant_id = '{TenantA.Id}' " +
            $"and actor_user_id = '{TestCaller.Bob}'")).Should().BeGreaterThan(0);
        (await _fx.SendAsync(TenantB, new GetAgentPolicyQuery(TestAgents.Assistant))).IsDefault.Should().BeTrue();
    }

    // ----- quotas and capacity ------------------------------------------------------------------------------------

    [Fact]
    public async Task StartingARun_ConsumesOneUnitOfThePlanQuota_AndIsRefusedWhenThePlanIsUsedUp()
    {
        _fx.Quota.Limits[EntitlementKeys.AgentRuns] = 1;
        var first = await _fx.StartAsync(TenantA);

        var second = () => _fx.StartAsync(TenantA, prompt: "one too many");

        var refused = (await second.Should().ThrowAsync<QuotaExceededException>()).Which;
        refused.ErrorCode.Should().Be("quota_exceeded");
        refused.Key.Should().Be(EntitlementKeys.AgentRuns);
        _fx.Quota.Used(EntitlementKeys.AgentRuns).Should().Be(1);
        (await _fx.AdminScalarAsync<long>("select count(*) from agentic.agent_runs where prompt = 'one too many'")).Should().Be(0);
        first.Status.Should().Be(AgentRunStatus.Queued);
    }

    [Fact]
    public async Task ModelTokens_AreCountedAgainstThePlan_AndARunStopsWhenThePlanRunsOut()
    {
        _fx.Quota.Limits[EntitlementKeys.AgentTokens] = 1; // the first model turn already needs more
        _fx.Board.Next = _ => ScriptedReply.Call(CreateNoteTool.Name, """{"title":"never written"}""");
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureCode.Should().Be("quota_exceeded");
        run.ModelTurns.Should().Be(1);
        _fx.World.Notes.Should().BeEmpty("the run stops before its next step");
    }

    [Fact]
    public async Task ModelTokens_OfACompletedRun_AreMetered()
    {
        _fx.Board.Play(ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}"""));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.RunOfAsync(TenantA, started.Id);
        run.Status.Should().Be(AgentRunStatus.Completed);
        _fx.Quota.Used(EntitlementKeys.AgentTokens).Should().Be(run.TotalTokens, "every saved turn is metered, the last one included");
    }

    [Fact]
    public async Task RunsInProgress_AreLimitedPerTenant_AndAFinishedRunFreesItsPlace()
    {
        _fx.Options.Limits.MaxConcurrentRunsPerTenant = 2;
        var first = await _fx.StartAsync(TenantA);
        await _fx.StartAsync(TenantA);

        var third = () => _fx.StartAsync(TenantA);
        (await third.Should().ThrowAsync<AgentRunLimitException>()).Which.ErrorCode.Should().Be("too_many_concurrent_runs");
        (await _fx.StartAsync(TenantB)).Status.Should().Be(AgentRunStatus.Queued, "capacity is per tenant");

        await _fx.RunAsync(TenantA, first.Id);

        (await _fx.StartAsync(TenantA)).Status.Should().Be(AgentRunStatus.Queued);
    }

    [Fact]
    public async Task ThePlan_CanLowerTheNumberOfRunsInProgress()
    {
        _fx.Options.Limits.MaxConcurrentRunsPerTenant = 5;
        _fx.Entitlements.Limits[EntitlementKeys.MaxConcurrentAgentRuns] = 1;
        await _fx.StartAsync(TenantA);

        var second = () => _fx.StartAsync(TenantA);

        await second.Should().ThrowAsync<AgentRunLimitException>();
    }

    // ----- usage --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Usage_ReportsWhatTheTenantsRunsUsed_PerAgent_AndNothingOfAnotherTenant()
    {
        var before = await _fx.SendAsync(TenantA, new GetAgentUsageQuery());
        var otherBefore = await _fx.SendAsync(TenantB, new GetAgentUsageQuery());
        _fx.Board.Play(ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}"""));
        var completed = await _fx.StartAsync(TenantA);
        await _fx.RunAsync(TenantA, completed.Id);
        await _fx.StartAsync(TenantA); // still queued

        var usage = await _fx.SendAsync(TenantA, new GetAgentUsageQuery());

        usage.To.Should().BeAfter(usage.From);
        (usage.Totals.Runs - before.Totals.Runs).Should().Be(2);
        (usage.Totals.Completed - before.Totals.Completed).Should().Be(1);
        (usage.Totals.Active - before.Totals.Active).Should().Be(1);
        (usage.Totals.ToolCalls - before.Totals.ToolCalls).Should().Be(1);
        (usage.Totals.ModelTurns - before.Totals.ModelTurns).Should().Be(2);
        (usage.Totals.InputTokens - before.Totals.InputTokens).Should().BeGreaterThan(0);
        usage.ByAgent.Should().Contain(a => a.AgentKey == TestAgents.Assistant);
        usage.ByAgent.Sum(a => a.Totals.Runs).Should().Be(usage.Totals.Runs);

        (await _fx.SendAsync(TenantB, new GetAgentUsageQuery())).Totals.Should().Be(otherBefore.Totals);
    }

    [Fact]
    public async Task Usage_OutsideThePeriod_IsNotCounted_AndAnInvalidPeriodFailsValidation()
    {
        await _fx.StartAsync(TenantA);

        var lastYear = await _fx.SendAsync(TenantA,
            new GetAgentUsageQuery(DateTime.UtcNow.AddYears(-2), DateTime.UtcNow.AddYears(-2).AddDays(30)));
        var inverted = () => _fx.SendAsync(TenantA, new GetAgentUsageQuery(DateTime.UtcNow, DateTime.UtcNow.AddDays(-1)));

        lastYear.Totals.Runs.Should().Be(0);
        lastYear.ByAgent.Should().BeEmpty();
        await inverted.Should().ThrowAsync<FluentValidation.ValidationException>();
    }
}
