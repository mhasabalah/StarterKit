using Agentic.Contracts.Agents;
using Agentic.Runtime.Tools;

namespace Agentic.Tests;

/// <summary>The authorization decision of a tool call: a pure function of trusted facts (no model text involved).</summary>
public class ToolPolicyTests
{
    private static readonly IReadOnlySet<string> AllPermissions =
        TestPermissions.Of(TestPermissions.NotesRead, TestPermissions.NotesWrite, TestPermissions.MailSend);

    private static (RegisteredAgent Agent, AgentRegistry Registry) Setup(AgentAutonomy autonomy = AgentAutonomy.Execute,
        params string[] tools)
    {
        var registry = TestAgents.Registry(TestAgents.Definition(autonomy: autonomy, tools: tools));
        return (registry.GetAgent("test.assistant"), registry);
    }

    [Fact]
    public void AnUnknownTool_IsRefused()
    {
        var (agent, _) = Setup();

        ToolPolicy.Decide(agent, null, AgentAutonomy.Execute, false, AllPermissions)
            .Should().Be(ToolDecision.Refuse(ToolPolicy.ToolNotAllowed));
    }

    [Fact]
    public void ARegisteredTool_OutsideTheAgentsAllowlist_IsRefused_ExactlyLikeAnUnknownOne()
    {
        var (agent, registry) = Setup(AgentAutonomy.Execute, "test.echo");

        ToolPolicy.Decide(agent, registry.FindTool("test.create_note"), AgentAutonomy.Execute, false, AllPermissions)
            .Should().Be(ToolDecision.Refuse(ToolPolicy.ToolNotAllowed));
    }

    [Fact]
    public void AToolWhosePermissionTheInitiatorLacks_IsRefused()
    {
        var (agent, registry) = Setup();

        ToolPolicy.Decide(agent, registry.FindTool("test.create_note"), AgentAutonomy.Execute, false,
                TestPermissions.Of(TestPermissions.NotesRead))
            .Should().Be(ToolDecision.Refuse(ToolPolicy.Forbidden));
    }

    [Fact]
    public void AnAnyMemberTool_NeedsNoPermission()
    {
        var (agent, registry) = Setup();

        ToolPolicy.Decide(agent, registry.FindTool("test.echo"), AgentAutonomy.Observe, false, TestPermissions.Of())
            .Should().Be(ToolDecision.Execute);
    }

    [Theory]
    [InlineData(AgentAutonomy.Observe)]
    [InlineData(AgentAutonomy.Recommend)]
    public void AMutation_AboveTheRunsAutonomy_IsRefused(AgentAutonomy autonomy)
    {
        var (agent, registry) = Setup();

        ToolPolicy.Decide(agent, registry.FindTool("test.create_note"), autonomy, false, AllPermissions)
            .Should().Be(ToolDecision.Refuse(ToolPolicy.AutonomyExceeded));
    }

    [Fact]
    public void ALowRiskMutation_RunsAtPrepare_ButASensitiveOneNeedsExecute()
    {
        var (agent, registry) = Setup();

        ToolPolicy.Decide(agent, registry.FindTool("test.create_note"), AgentAutonomy.Prepare, false, AllPermissions)
            .Should().Be(ToolDecision.Execute);
        ToolPolicy.Decide(agent, registry.FindTool("test.publish_note"), AgentAutonomy.Prepare, false, AllPermissions)
            .Should().Be(ToolDecision.Refuse(ToolPolicy.AutonomyExceeded));
    }

    [Theory]
    [InlineData("test.publish_note")]
    [InlineData("test.send_email")]
    public void SensitiveAndExternalActions_AlwaysNeedApproval_EvenAtFullAutonomy(string tool)
    {
        var (agent, registry) = Setup();

        ToolPolicy.Decide(agent, registry.FindTool(tool), AgentAutonomy.Execute, false, AllPermissions)
            .Should().Be(ToolDecision.RequireApproval);
    }

    [Fact]
    public void OnceTheRunReadUntrustedContent_EveryMutationNeedsApproval_ButReadsStillRun()
    {
        var (agent, registry) = Setup();

        ToolPolicy.Decide(agent, registry.FindTool("test.create_note"), AgentAutonomy.Execute, tainted: true, AllPermissions)
            .Should().Be(ToolDecision.RequireApproval);
        ToolPolicy.Decide(agent, registry.FindTool("test.echo"), AgentAutonomy.Execute, tainted: true, AllPermissions)
            .Should().Be(ToolDecision.Execute);
    }

    [Fact]
    public void PermissionIsCheckedBeforeApproval_SoAnUnauthorizedInitiatorNeverReachesAnApprover()
    {
        var (agent, registry) = Setup();

        ToolPolicy.Decide(agent, registry.FindTool("test.send_email"), AgentAutonomy.Execute, false,
                TestPermissions.Of(TestPermissions.NotesWrite))
            .Should().Be(ToolDecision.Refuse(ToolPolicy.Forbidden));
    }
}
