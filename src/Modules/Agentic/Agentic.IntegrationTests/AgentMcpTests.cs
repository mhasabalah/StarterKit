using Agentic.Contracts.Agents;
using Agentic.Contracts.Runs;
using Agentic.Data;
using Agentic.Domains.AgentRuns.Models;
using Agentic.Runtime.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Entitlements;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>
/// The MCP server's gateway on PostgreSQL (ADR-0034): an external client gets only the tools of an MCP agent that the
/// signed-in person may run now without an approval; every call it makes is decided like a call of a tainted run and
/// recorded as a run with trigger Mcp, inside the person's tenant.
/// </summary>
[Collection(AgenticPostgresCollection.Name)]
public class AgentMcpTests
{
    private readonly AgenticPostgresFixture _fx;

    public AgentMcpTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
    }

    private Task<IReadOnlyList<McpTool>?> ListAsync(TenantInfo tenant, string agentKey = TestAgents.Mcp, Guid? user = null,
        IReadOnlyCollection<string>? apiKeyScopes = null) =>
        _fx.AsCallerAsync(tenant, sp => sp.GetRequiredService<AgentMcpGateway>().ListAsync(agentKey, CancellationToken.None),
            user, apiKeyScopes);

    private Task<McpToolResult> CallAsync(TenantInfo tenant, string tool, string arguments, string agentKey = TestAgents.Mcp,
        Guid? user = null) =>
        _fx.AsCallerAsync(tenant, sp => sp.GetRequiredService<AgentMcpGateway>()
            .CallAsync(agentKey, tool, arguments, CancellationToken.None), user);

    private Task<List<AgentRun>> McpRunsAsync(TenantInfo tenant, Guid user) =>
        _fx.InTenantAsync(tenant, sp => sp.GetRequiredService<AgenticDbContext>().Runs.AsNoTracking()
            .Where(r => r.TriggerKind == AgentTriggerKind.Mcp && r.InitiatedBy == user).ToListAsync());

    [Fact]
    public async Task AClient_IsOfferedTheReadTools_NeverAChange()
    {
        var tools = await ListAsync(TenantA);

        tools!.Select(t => t.Name).Should().BeEquivalentTo([GetItemTool.Name, ReadDocumentTool.Name, SlowLookupTool.Name]);
        tools.Should().OnlyContain(t => t.ReadOnly);
        tools.Single(t => t.Name == GetItemTool.Name).InputSchema.GetProperty("properties").TryGetProperty("code", out _)
            .Should().BeTrue();
    }

    [Fact]
    public async Task ACall_RunsTheToolAsThePerson_AndIsRecordedAsAnMcpRun_WithoutEventsOrNotifications()
    {
        var user = Guid.NewGuid();

        var result = await CallAsync(TenantA, GetItemTool.Name, """{"code":"A-1"}""", user: user);

        result.IsError.Should().BeFalse(result.Json);
        result.Json.Should().Contain("A-1");
        var run = (await McpRunsAsync(TenantA, user)).Should().ContainSingle().Subject;
        run.Id.Should().Be(result.RunId!.Value);
        run.Status.Should().Be(AgentRunStatus.Completed);
        run.AgentKey.Should().Be(TestAgents.Mcp);
        run.ToolCalls.Should().Be(1);
        run.ModelTurns.Should().Be(0);
        run.InputJson.Should().Contain("A-1");
        var step = (await _fx.StepsOfAsync(TenantA, run.Id)).Should().ContainSingle().Subject;
        step.Status.Should().Be(AgentRunStepStatus.Succeeded);
        step.ToolName.Should().Be(GetItemTool.Name);
        _fx.World.Observations.Should().Contain(o => o.Tool == GetItemTool.Name && o.UserId == user && o.TenantId == TenantA.Id);
        (await _fx.OutboxEventsAsync(run.Id)).Should().BeEmpty("no module waits for an MCP call");
        _fx.Notifications.InApp.Should().NotContain(n => n.CorrelationId == run.Id);
    }

    [Theory]
    [InlineData(CreateNoteTool.Name, """{"title":"x"}""", "approval_required")]
    [InlineData(SendMailTool.Name, """{"title":"x"}""", "approval_required")]
    [InlineData("test.append_log", """{"title":"x"}""", "tool_not_found")]
    [InlineData("test.unknown", "{}", "tool_not_found")]
    public async Task AChange_OrAToolThatIsNotTheAgents_IsRefused_AndNothingRunsOrIsRecorded(string tool, string arguments,
        string code)
    {
        var user = Guid.NewGuid();

        var result = await CallAsync(TenantA, tool, arguments, user: user);

        result.IsError.Should().BeTrue();
        result.Json.Should().Contain(code);
        (await McpRunsAsync(TenantA, user)).Should().BeEmpty();
        _fx.World.Executions(CreateNoteTool.Name).Should().Be(0);
    }

    [Fact]
    public async Task InvalidArguments_AreRefusedBeforeAnythingIsRecorded()
    {
        var user = Guid.NewGuid();

        var result = await CallAsync(TenantA, GetItemTool.Name, """{"code":"A-1","tenantId":"x"}""", user: user);

        result.IsError.Should().BeTrue();
        result.Json.Should().Contain("invalid_arguments");
        (await McpRunsAsync(TenantA, user)).Should().BeEmpty();
    }

    [Fact]
    public async Task APersonWhoMayNotStartTheAgent_GetsNoServer_AndAnAgentThatIsNotAnMcpServerHasNone()
    {
        _fx.Caller.Denied.Add(TenantPermissions.WebhooksManage);

        (await ListAsync(TenantA, TestAgents.McpRestricted)).Should().BeNull();
        (await ListAsync(TenantA, TestAgents.Assistant)).Should().BeNull();
        var call = () => CallAsync(TenantA, GetItemTool.Name, """{"code":"A-1"}""", TestAgents.McpRestricted);
        await call.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task WithMcpOff_NoAgentHasAServer()
    {
        _fx.Options.Mcp.Enabled = false;

        (await ListAsync(TenantA)).Should().BeNull();
    }

    [Fact]
    public async Task ACallCountsAgainstThePlansRuns()
    {
        _fx.Quota.Limits[EntitlementKeys.AgentRuns] = _fx.Quota.Used(EntitlementKeys.AgentRuns);

        var call = () => CallAsync(TenantA, GetItemTool.Name, """{"code":"A-1"}""");

        await call.Should().ThrowAsync<QuotaExceededException>();
    }

    [Fact]
    public async Task ACall_StaysInsideItsTenant()
    {
        var user = Guid.NewGuid();

        var result = await CallAsync(TenantB, GetItemTool.Name, """{"code":"B-1"}""", user: user);

        (await McpRunsAsync(TenantB, user)).Should().ContainSingle();
        (await McpRunsAsync(TenantA, user)).Should().BeEmpty();
        var read = () => _fx.SendAsync(TenantA, new GetAgentRunByIdQuery(result.RunId!.Value), user);
        await read.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ASlowTool_TimesOut_AsAnErrorTheClientIsTold()
    {
        var result = await CallAsync(TenantA, SlowLookupTool.Name, """{"code":"A-1"}""");

        result.IsError.Should().BeTrue();
        result.Json.Should().Contain("timeout");
    }

    [Fact]
    public async Task AnMcpCallFoundByAWorker_IsFailed_NeverRunAsAnAgentRun()
    {
        var user = Guid.NewGuid();
        var result = await CallAsync(TenantA, GetItemTool.Name, """{"code":"A-1"}""", user: user);
        await _fx.AdminExecAsync($"update agentic.agent_runs set status = 'Queued', finished_at = null where id = '{result.RunId}'");
        var turnsBefore = _fx.Board.Turns;

        await _fx.RunAsync(TenantA, result.RunId!.Value);

        var run = await _fx.RunOfAsync(TenantA, result.RunId.Value);
        run.Status.Should().Be(AgentRunStatus.Failed);
        run.FailureCode.Should().Be("mcp_call_interrupted");
        _fx.Board.Turns.Should().Be(turnsBefore, "no model is asked");
    }
}
