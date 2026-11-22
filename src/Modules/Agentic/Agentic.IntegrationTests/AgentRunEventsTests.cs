using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Domains.AgentRuns.EventHandlers;
using Agentic.Runtime.Execution;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>
/// What a run tells the outside: integration events through the transactional outbox, in-app notifications, and live
/// progress events. None of them is the source of truth, and none carries prompts, arguments or results.
/// </summary>
[Collection(AgenticPostgresCollection.Name)]
public class AgentRunEventsTests
{
    private readonly AgenticPostgresFixture _fx;

    public AgentRunEventsTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
    }

    private static int Status(Notification.Contracts.Realtime.UserEvent e) => (int)e.Data["status"]!;

    [Fact]
    public async Task AnAnswer_IsStreamedToTheInitiator_InOrder_AndTheStreamAddsUpToTheSavedAnswer()
    {
        _fx.Options.Streaming.IntervalMilliseconds = 0; // every piece is sent, so the order is visible
        _fx.Board.Play(ScriptedReply.Say("The item A-1 is in stock and ships today."));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var run = await _fx.SendAsync(TenantA, new GetAgentRunByIdQuery(started.Id));
        var text = _fx.Events.For(started.Id, AgentRunTextStreams.EventType);
        text.Should().HaveCountGreaterThan(1).And.OnlyContain(e => e.User == TestCaller.Alice);
        text.Select(e => (int)e.Event.Data["offset"]!).Should().BeInAscendingOrder();
        string.Concat(text.Select(e => (string)e.Event.Data["delta"]!)).Should().Be(run.OutputText);
        _fx.Events.For(started.Id).Should().OnlyContain(e => !e.Event.Data.ContainsKey("delta"),
            "progress events still carry no content");
    }

    [Fact]
    public async Task WithStreamingOff_NoTextIsSent()
    {
        _fx.Options.Streaming.Enabled = false;
        _fx.Board.Play(ScriptedReply.Say("Done."));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        _fx.Events.For(started.Id, AgentRunTextStreams.EventType).Should().BeEmpty();
    }

    [Fact]
    public async Task ACompletedRun_WritesOneCompletedEvent_ToTheOutbox_InTheFinishingTransaction()
    {
        _fx.Board.Play(ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}"""));
        var started = await _fx.StartAsync(TenantA);

        (await _fx.OutboxEventsAsync(started.Id)).Should().BeEmpty("nothing is announced before the run finished");
        await _fx.RunAsync(TenantA, started.Id);
        await _fx.RunAsync(TenantA, started.Id); // duplicate delivery

        var events = await _fx.OutboxEventsAsync(started.Id);
        events.Should().ContainSingle().Which.Should().Contain("agent-run-completed");
    }

    [Fact]
    public async Task AFailedRun_WritesAFailedEvent_AndACancelledRunWritesNone()
    {
        _fx.Options.Limits.MaxModelTurns = 1;
        _fx.Board.Next = _ => ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}""");
        var failed = await _fx.StartAsync(TenantA);
        await _fx.RunAsync(TenantA, failed.Id);
        var cancelled = await _fx.StartAsync(TenantA);
        await _fx.SendAsync(TenantA, new CancelAgentRunCommand(cancelled.Id));

        (await _fx.OutboxEventsAsync(failed.Id)).Should().ContainSingle().Which.Should().Contain("agent-run-failed");
        (await _fx.OutboxEventsAsync(cancelled.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheInitiator_IsNotified_WhenTheirRunFinishes_AndWhenItNeedsADecision()
    {
        _fx.Board.Play(ScriptedReply.Call(SendMailTool.Name, """{"title":"Invoice"}"""));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var waiting = _fx.Notifications.InApp.Should().ContainSingle().Subject;
        waiting.User.Should().Be(TestCaller.Alice);
        waiting.Content.Category.Should().Be(AgentNotificationCategories.ApprovalRequired);
        waiting.Content.Data!["tool"].Should().Be(SendMailTool.Name);
        waiting.Content.Body.En.Should().NotContain("Invoice", "the proposed payload is not put into a notification");
        var approval = await _fx.ApprovalOfAsync(TenantA, started.Id);
        waiting.CorrelationId.Should().Be(approval.Id);

        await _fx.ApproveAndRunAsync(TenantA, started.Id, TestCaller.Bob);

        var finished = _fx.Notifications.InApp.Should().HaveCount(2).And.Subject.Last();
        finished.User.Should().Be(TestCaller.Alice);
        finished.Content.Category.Should().Be(AgentNotificationCategories.RunFinished);
        finished.Content.Data!["status"].Should().Be(nameof(AgentRunStatus.Completed));
        finished.CorrelationId.Should().Be(started.Id);
    }

    [Fact]
    public async Task Progress_FollowsTheRun_FromStartThroughSuspensionAndResumeToTheEnd_AndGoesOnlyToTheInitiator()
    {
        _fx.Board.Play(
            ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}"""),
            ScriptedReply.Call(SendMailTool.Name, """{"title":"secret customer text"}"""));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);
        await _fx.ApproveAndRunAsync(TenantA, started.Id, TestCaller.Bob);

        var events = _fx.Events.For(started.Id);
        events.Should().OnlyContain(e => e.User == TestCaller.Alice && e.Event.Type == AgentRunProgressPublisher.EventType &&
                                         e.Event.TenantId == TenantA.Id);
        var statuses = events.Select(e => Status(e.Event)).ToList();
        statuses.First().Should().Be((int)AgentRunStatus.Running, "the first event says the run started");
        statuses.Should().Contain((int)AgentRunStatus.AwaitingApproval);
        statuses.Last().Should().Be((int)AgentRunStatus.Completed);
        statuses.IndexOf((int)AgentRunStatus.AwaitingApproval).Should().BeLessThan(statuses.LastIndexOf((int)AgentRunStatus.Running),
            "the run is reported as running again after the approval");
        events.Select(e => e.Event.Data["tool"]).Should().Contain(GetItemTool.Name).And.Contain(SendMailTool.Name);

        // Ids, names, counters and states only.
        var everything = string.Join(" ", events.SelectMany(e => e.Event.Data.Values).Select(v => v?.ToString()));
        everything.Should().NotContain("secret customer text").And.NotContain("A-1");
    }

    [Fact]
    public async Task OrdinaryProgress_IsThrottled_ButAChangeOfStatusIsAlwaysSent()
    {
        _fx.Options.ProgressIntervalMilliseconds = 60_000; // the clock does not move during the run
        _fx.Board.Play(
            ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}"""),
            ScriptedReply.Call(GetItemTool.Name, """{"code":"A-2"}"""),
            ScriptedReply.Call(GetItemTool.Name, """{"code":"A-3"}"""));
        var started = await _fx.StartAsync(TenantA);

        await _fx.RunAsync(TenantA, started.Id);

        var statuses = _fx.Events.For(started.Id).Select(e => Status(e.Event)).ToList();
        statuses.Should().Equal((int)AgentRunStatus.Running, (int)AgentRunStatus.Completed);
    }
}
