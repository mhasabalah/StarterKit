using System.Text.Json;
using Agentic.Contracts.Agents;
using Agentic.Contracts.Runs;
using Agentic.Contracts.Tools;
using Agentic.Domains.AgentRuns.Exceptions;
using Agentic.Domains.AgentRuns.Models;
using Agentic.Runtime.Execution;
using Agentic.Runtime.Tools;
using Shared.Contracts.Localization;

namespace Agentic.Tests;

/// <summary>Delegation (ADR-0033): what the registry refuses at startup, the declaration, the arguments, the run rules.</summary>
public class AgentDelegationTests
{
    private static AgentDefinition Agent(string key, string[]? delegates = null, AgentTriggerKind[]? triggers = null) => new()
    {
        Key = key,
        Name = new LocalizedText(key, key),
        Instructions = "Work.",
        Delegates = delegates ?? [],
        AllowedTriggers = triggers ?? [AgentTriggerKind.User, AgentTriggerKind.Delegated]
    };

    private static Action Building(params AgentDefinition[] agents) => () => _ = new AgentRegistry([], agents);

    [Fact]
    public void ADelegateNoModuleRegistered_StopsTheHost() =>
        Building(Agent("test.a", ["test.missing"])).Should().Throw<InvalidOperationException>().WithMessage("*test.missing*");

    [Fact]
    public void AnAgentDelegatingToItself_StopsTheHost() =>
        Building(Agent("test.a", ["test.a"])).Should().Throw<InvalidOperationException>().WithMessage("*itself*");

    [Fact]
    public void ADelegateThatDoesNotAllowTheDelegatedTrigger_StopsTheHost() =>
        Building(Agent("test.a", ["test.b"]), Agent("test.b", triggers: [AgentTriggerKind.User]))
            .Should().Throw<InvalidOperationException>().WithMessage("*Delegated*");

    [Fact]
    public void AgentsThatDelegateInACycle_StopTheHost() =>
        Building(Agent("test.a", ["test.b"]), Agent("test.b", ["test.c"]), Agent("test.c", ["test.a"]))
            .Should().Throw<InvalidOperationException>().WithMessage("*cycle*");

    [Fact]
    public void AToolNamedLikeTheDelegateTool_IsReserved() =>
        AgentToolNames.Delegate.Should().Be("agent.delegate");

    [Fact]
    public void AnAgentWithDelegates_IsGivenTheDelegateTool_ListingOnlyItsDelegates()
    {
        var registry = new AgentRegistry([], [Agent("test.a", ["test.b"]), Agent("test.b"), Agent("test.c")]);
        var run = AgentRun.Start("test.a", 1, "default", AgentAutonomy.Recommend, AgentTriggerKind.User, Guid.NewGuid(),
            "Go", null, new AgentRunBudget(5, 5, 1_000, 60), "en");

        var request = TranscriptBuilder.Build(registry.GetAgent("test.a"), run, []);
        var withoutDelegates = TranscriptBuilder.Build(registry.GetAgent("test.b"), run, []);

        var tool = request.Tools.Should().ContainSingle(t => t.Name == AgentToolNames.Delegate).Subject;
        tool.Description.Should().Contain("test.b").And.NotContain("test.c");
        tool.Schema.GetProperty("properties").GetProperty("agentKey").GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString()).Should().Equal("test.b");
        withoutDelegates.Tools.Should().NotContain(t => t.Name == AgentToolNames.Delegate);
    }

    [Theory]
    [InlineData("""{"agentKey":"test.b","task":"Look it up"}""", true)]
    [InlineData("""{"agentKey":"test.b","task":"   "}""", false)]
    [InlineData("""{"agentKey":"test.b"}""", false)]
    [InlineData("""{"agentKey":"test.b","task":"x","tenantId":"t"}""", false)]
    [InlineData("""not json""", false)]
    public void TheModelsArguments_AreExactlyAnAgentAndATask(string json, bool valid)
    {
        AgentDelegation.TryParse(json, 100, out var arguments, out var error).Should().Be(valid);
        if (valid)
            arguments.Should().Be(new AgentDelegation.Arguments("test.b", "Look it up"));
        else
            error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ADelegatedRun_NeedsItsParentStepAndADepth_AndIsNeverATurnOfAConversation()
    {
        var budget = new AgentRunBudget(5, 5, 1_000, 60);
        AgentRun Start(Guid? parent, Guid? step, int depth, Guid? conversation = null) => AgentRun.Start("test.b", 1, "default",
            AgentAutonomy.Recommend, AgentTriggerKind.Delegated, Guid.NewGuid(), "Go", null, budget, "en",
            conversationId: conversation, conversationTurn: conversation is null ? null : 1, parentRunId: parent,
            parentStepId: step, delegationDepth: depth);

        Start(Guid.NewGuid(), Guid.NewGuid(), 1).Depth.Should().Be(1);
        FluentActions.Invoking(() => Start(Guid.NewGuid(), null, 1)).Should().Throw<InvalidAgentRunStateException>();
        FluentActions.Invoking(() => Start(Guid.NewGuid(), Guid.NewGuid(), 0)).Should().Throw<InvalidAgentRunStateException>();
        FluentActions.Invoking(() => Start(null, null, 1)).Should().Throw<InvalidAgentRunStateException>();
        FluentActions.Invoking(() => Start(Guid.NewGuid(), Guid.NewGuid(), AgentRun.MaxDepth + 1))
            .Should().Throw<InvalidAgentRunStateException>();
        FluentActions.Invoking(() => Start(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid()))
            .Should().Throw<InvalidAgentRunStateException>();
    }

    [Fact]
    public void ARunWaitingForItsDelegate_HoldsNoLease_IsNotClaimed_AndIsCancelledAtOnce()
    {
        var now = new DateTime(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);
        var run = AgentRun.Start("test.a", 1, "default", AgentAutonomy.Recommend, AgentTriggerKind.User, Guid.NewGuid(),
            "Go", null, new AgentRunBudget(5, 5, 1_000, 60), "en");
        run.TryClaim("worker", now, TimeSpan.FromMinutes(5)).Should().BeTrue();
        var turn = run.RecordModelTurn(null, "m", "tool_calls", 10, 5, 0, 1, now);
        var step = run.PlanToolCall(turn, "c1", AgentToolNames.Delegate, "{}");

        run.AwaitDelegate(step);

        run.Status.Should().Be(AgentRunStatus.AwaitingDelegate);
        run.LeaseOwner.Should().BeNull();
        run.TryClaim("other", now, TimeSpan.FromMinutes(5)).Should().BeFalse();
        run.CompleteDelegation(step, """{"ok":true}""", true, true, 100, 50, 0.1m, 10, now).Should().BeTrue();
        run.Status.Should().Be(AgentRunStatus.Queued);
        run.Tainted.Should().BeTrue();
        run.InputTokens.Should().Be(110);
        run.ToolCalls.Should().Be(1);
        run.CompleteDelegation(step, "{}", true, false, 1, 1, 0, 0, now).Should().BeFalse("only once");

        run.TryClaim("worker", now, TimeSpan.FromMinutes(5));
        var second = run.PlanToolCall(run.RecordModelTurn(null, "m", "tool_calls", 1, 1, 0, 1, now), "c2", AgentToolNames.Delegate, "{}");
        run.AwaitDelegate(second);
        run.RequestCancel(now);
        run.Status.Should().Be(AgentRunStatus.Cancelled);
    }
}
