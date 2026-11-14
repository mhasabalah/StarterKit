using System.Collections.Concurrent;
using System.Diagnostics;
using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Telemetry;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>
/// One run is one trace: the worker's spans join the trace of the request that started the run, and every model
/// request and tool call is a child of the attempt that made it, so an operator follows a run from the API call to each
/// action.
/// </summary>
[Collection(AgenticPostgresCollection.Name)]
public class AgentRunTracingTests
{
    private static readonly ActivitySource Request = new("Agentic.IntegrationTests.Request");
    private readonly AgenticPostgresFixture _fx;

    public AgentRunTracingTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
    }

    [Fact]
    public async Task TheModelTurnsAndToolCalls_OfARun_AreChildrenOfItsSpan_InTheTraceOfTheRequestThatStartedIt()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true, // the assertions filter by run id and source
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add
        };
        ActivitySource.AddActivityListener(listener);
        _fx.Board.Play(
            ScriptedReply.Call(GetItemTool.Name, """{"code":"A-1"}"""),
            ScriptedReply.Say("The item is Item A-1."));

        Guid runId;
        ActivityTraceId requestTrace;
        using (var request = Request.StartActivity("POST /agent-runs"))
        {
            requestTrace = request!.TraceId;
            runId = (await _fx.StartAsync(TenantA)).Id;
        }

        await _fx.RunAsync(TenantA, runId); // a worker, outside the request
        (await _fx.RunOfAsync(TenantA, runId)).Status.Should().Be(AgentRunStatus.Completed);

        var ofRun = stopped.Where(a => Equals(a.GetTagItem("agent.run.id"), runId)).ToList();
        var attempt = ofRun.Should().ContainSingle(a => a.OperationName == "agent.run").Subject;
        var turns = ofRun.Where(a => a.OperationName == "agent.model_turn").ToList();
        var tool = ofRun.Should().ContainSingle(a => a.OperationName == "agent.tool").Subject;

        attempt.TraceId.Should().Be(requestTrace, "the run is found from the API call that created it");
        turns.Should().HaveCount(2);
        turns.Should().OnlyContain(t => t.TraceId == requestTrace && t.ParentSpanId == attempt.SpanId);
        tool.TraceId.Should().Be(requestTrace);
        tool.ParentSpanId.Should().Be(attempt.SpanId);
        tool.GetTagItem("agent.tool.name").Should().Be(GetItemTool.Name);

        // The provider client's gen_ai span sits under each model turn.
        var turnSpans = turns.Select(t => t.SpanId).ToHashSet();
        stopped.Where(a => a.Source.Name == AgenticTelemetry.SourceName && turnSpans.Contains(a.ParentSpanId))
            .Should().HaveCount(2);
    }
}
