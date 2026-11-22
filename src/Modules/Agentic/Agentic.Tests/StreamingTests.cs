using System.Runtime.CompilerServices;
using Agentic.Contracts.Agents;
using Agentic.Contracts.Scripting;
using Agentic.Domains.AgentRuns.Models;
using Agentic.Options;
using Agentic.Runtime.Execution;
using Agentic.Runtime.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Notification.Contracts.Realtime;

namespace Agentic.Tests;

/// <summary>
/// Streaming (ADR-0030): the gateway streams to a sink without changing the response, and the run's text stream sends
/// the answer to the initiator in ordered pieces, best effort.
/// </summary>
public class StreamingTests
{
    private static readonly Guid User = Guid.Parse("11111111-aaaa-0000-0000-0000000000a1");

    private sealed class SayScript(string text, bool callTool = false) : IAgentScript
    {
        public string AgentKey => "test.assistant";

        public ScriptedReply Next(ScriptedTurn turn) => callTool
            ? new ScriptedReply(text, [new ScriptedToolCall("test.echo", """{"text":"hi"}""")])
            : ScriptedReply.Say(text);
    }

    private sealed class RecordingSink : IModelTextSink
    {
        public List<string> Deltas { get; } = [];

        public ValueTask WriteAsync(string delta, CancellationToken cancellationToken)
        {
            Deltas.Add(delta);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A provider that streams but reports no usage (some self-hosted servers).</summary>
    private sealed class NoUsageWhenStreaming(IChatClient inner) : DelegatingChatClient(inner)
    {
        public int Streamed { get; private set; }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Streamed++;
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                update.Contents = update.Contents.Where(c => c is not UsageContent).ToList();
                yield return update;
            }
        }
    }

    private static AgenticOptions Settings() => new()
    {
        Enabled = true,
        Models = { ["default"] = new AgentModelOptions { Provider = "scripted", ModelId = "scripted" } },
        Providers = { ["scripted"] = new AgentProviderOptions { Kind = AgentProviderKind.Scripted } }
    };

    private static (ChatClientModelGateway Gateway, AgentChatClientFactory Factory) Gateway(IAgentScript script,
        AgenticOptions? settings = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(settings ?? Settings());
        var factory = new AgentChatClientFactory(options, [script], NullLoggerFactory.Instance);
        return (new ChatClientModelGateway(factory, options), factory);
    }

    private static ModelRequest Request() =>
        new("default", "test.assistant", "Be helpful.", [new ModelMessage(ModelRole.User, "Hello")], []);

    [Fact]
    public async Task AStreamedAnswer_IsTheSameResponseAsAnUnstreamedOne_AndTheSinkGetsTheTextInPieces()
    {
        const string text = "The shipment is ready for clearance today.";
        var script = new SayScript(text, callTool: true);
        var sink = new RecordingSink();

        var plain = await Gateway(script).Gateway.CompleteAsync(Request(), CancellationToken.None);
        var streamed = await Gateway(script).Gateway.CompleteAsync(Request(), sink, CancellationToken.None);

        sink.Deltas.Should().HaveCountGreaterThan(1, "the answer arrives word by word");
        string.Concat(sink.Deltas).Should().Be(text);
        streamed.Text.Should().Be(plain.Text);
        streamed.ToolCalls.Should().BeEquivalentTo(plain.ToolCalls);
        streamed.InputTokens.Should().Be(plain.InputTokens).And.BePositive();
        streamed.OutputTokens.Should().Be(plain.OutputTokens);
        streamed.FinishReason.Should().Be(plain.FinishReason);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task WithStreamingOff_GloballyOrForTheModel_TheSinkGetsNothing(bool enabled, bool modelStreams)
    {
        var settings = Settings();
        settings.Streaming.Enabled = enabled;
        settings.Models["default"].Streaming = modelStreams;
        var sink = new RecordingSink();

        var response = await Gateway(new SayScript("Hello there"), settings).Gateway.CompleteAsync(Request(), sink, CancellationToken.None);

        response.Text.Should().Be("Hello there");
        sink.Deltas.Should().BeEmpty();
    }

    [Fact]
    public async Task AProviderWithoutUsageWhileStreaming_IsCharged_AnEstimate_AndIsNoLongerStreamed()
    {
        var (gateway, factory) = Gateway(new SayScript("A fairly long answer with several words in it"));
        var provider = new NoUsageWhenStreaming(factory.Get("default"));
        factory.Use("default", provider);

        var first = await gateway.CompleteAsync(Request(), new RecordingSink(), CancellationToken.None);
        var secondSink = new RecordingSink();
        var second = await gateway.CompleteAsync(Request(), secondSink, CancellationToken.None);

        first.InputTokens.Should().BePositive("a streamed turn without usage must not look free");
        first.OutputTokens.Should().BePositive();
        provider.Streamed.Should().Be(1, "the model is no longer streamed after it reported no usage");
        secondSink.Deltas.Should().BeEmpty();
        second.OutputTokens.Should().BePositive("the unstreamed request reports usage");
    }

    // ----- the run's text stream ------------------------------------------------------------------------------

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = 1;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    private sealed class Events(bool fail = false) : IUserEventPublisher
    {
        public List<(Guid User, UserEvent Event)> Sent { get; } = [];

        public Task PublishAsync(Guid userId, UserEvent userEvent, CancellationToken cancellationToken = default)
        {
            if (fail)
                throw new InvalidOperationException("hub down");
            Sent.Add((userId, userEvent));
            return Task.CompletedTask;
        }
    }

    private static AgentRun Run(AgentTriggerKind trigger = AgentTriggerKind.User) =>
        AgentRun.Start("test.assistant", 1, "default", AgentAutonomy.Recommend, trigger, User, "Hello", null,
            new AgentRunBudget(5, 5, 10_000, 60), "en", null);

    private static AgentRunTextStreams Streams(IUserEventPublisher events, TimeProvider clock, Action<AgenticOptions>? configure = null)
    {
        var settings = Settings();
        settings.Streaming.IntervalMilliseconds = 100;
        configure?.Invoke(settings);
        return new AgentRunTextStreams(events, Shared.Core.MultiTenancy.TenantContext.SingleTenant, clock, Microsoft.Extensions.Options.Options.Create(settings),
            NullLogger<AgentRunTextStreams>.Instance);
    }

    [Fact]
    public async Task TheTextStream_SendsAtMostOncePerInterval_WithConsecutiveOffsets_AndTheRestAtTheEnd()
    {
        var events = new Events();
        var clock = new ManualClock();
        var run = Run();

        await using (var stream = Streams(events, clock).Open(run)!)
        {
            await stream.WriteAsync("The ", CancellationToken.None);
            await stream.WriteAsync("shipment ", CancellationToken.None);
            clock.Advance(TimeSpan.FromMilliseconds(150));
            await stream.WriteAsync("is ", CancellationToken.None);
            await stream.WriteAsync("ready.", CancellationToken.None);
        }

        events.Sent.Should().HaveCount(2);
        events.Sent.Should().OnlyContain(e => e.User == User && e.Event.Type == AgentRunTextStreams.EventType);
        events.Sent.Select(e => (string)e.Event.Data["delta"]!).Should().Equal("The shipment is ", "ready.");
        events.Sent.Select(e => (int)e.Event.Data["offset"]!).Should().Equal(0, 16);
        events.Sent.Should().OnlyContain(e => (int)e.Event.Data["turn"]! == 1 && (Guid)e.Event.Data["runId"]! == run.Id);
    }

    [Fact]
    public async Task AnAnswerAboveTheSizeLimit_StopsBeingStreamed()
    {
        var events = new Events();
        await using (var stream = Streams(events, new ManualClock(), s => s.Limits.MaxModelTextBytes = 10).Open(Run())!)
        {
            await stream.WriteAsync("12345", CancellationToken.None);
            await stream.WriteAsync("678901", CancellationToken.None);
        }

        events.Sent.Should().BeEmpty("the run never keeps an oversized answer, so it is not previewed either");
    }

    [Theory]
    [InlineData(AgentTriggerKind.Event)]
    [InlineData(AgentTriggerKind.Schedule)]
    public void RunsNobodyWatches_DoNotStream(AgentTriggerKind trigger) =>
        Streams(new Events(), new ManualClock()).Open(Run(trigger)).Should().BeNull();

    [Fact]
    public void WithStreamingOff_NoRunStreams() =>
        Streams(new Events(), new ManualClock(), s => s.Streaming.Enabled = false).Open(Run()).Should().BeNull();

    [Fact]
    public async Task AFailedSend_NeverFailsTheTurn()
    {
        var stream = Streams(new Events(fail: true), new ManualClock(), s => s.Streaming.IntervalMilliseconds = 0).Open(Run())!;

        var act = async () =>
        {
            await stream.WriteAsync("Hello", CancellationToken.None);
            await stream.DisposeAsync();
        };

        await act.Should().NotThrowAsync();
    }
}
