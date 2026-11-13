using System.Text.Json;
using Agentic.Contracts.Agents;
using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Contracts.Tools;
using Agentic.Domains.AgentRuns.Models;
using Agentic.Options;
using Agentic.Runtime.Execution;
using Agentic.Runtime.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agentic.Tests;

/// <summary>
/// The model gateway over the scripted provider: the same conversion path (messages, tool declarations, tool calls,
/// usage) the real providers go through, without a network.
/// </summary>
public class ModelGatewayTests
{
    private sealed class RecordingScript(Func<ScriptedTurn, ScriptedReply> next) : IAgentScript
    {
        public List<ScriptedTurn> Turns { get; } = [];

        public string AgentKey => "test.assistant";

        public ScriptedReply Next(ScriptedTurn turn)
        {
            Turns.Add(turn);
            return next(turn);
        }
    }

    private static AgenticOptions Settings() => new()
    {
        Enabled = true,
        Models =
        {
            ["default"] = new AgentModelOptions
            {
                Provider = "scripted", ModelId = "scripted", InputPricePerMillionTokens = 2_000_000m,
                OutputPricePerMillionTokens = 4_000_000m
            }
        },
        Providers = { ["scripted"] = new AgentProviderOptions { Kind = AgentProviderKind.Scripted } }
    };

    private static ChatClientModelGateway Gateway(IAgentScript script, AgenticOptions? settings = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(settings ?? Settings());
        return new ChatClientModelGateway(new AgentChatClientFactory(options, [script], NullLoggerFactory.Instance), options);
    }

    private static readonly JsonElement AnySchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    private static ModelRequest Request(params ModelMessage[] messages) => new("default", "test.assistant", "Be helpful.",
        messages, [new ModelToolDeclaration("test.echo", "Repeats a text.", AnySchema)]);

    [Fact]
    public async Task Complete_ReturnsToolCalls_AsData_WithTheirRegisteredNames()
    {
        var script = new RecordingScript(_ => ScriptedReply.Call("test.echo", """{"text":"hi","times":2}"""));

        var response = await Gateway(script).CompleteAsync(Request(new ModelMessage(ModelRole.User, "Say hi twice")),
            CancellationToken.None);

        var call = response.ToolCalls.Should().ContainSingle().Subject;
        call.ToolName.Should().Be("test.echo");
        call.CallId.Should().NotBeNullOrWhiteSpace();
        JsonDocument.Parse(call.ArgumentsJson).RootElement.GetProperty("times").GetInt32().Should().Be(2);
        response.Text.Should().BeNull();
        response.ModelId.Should().Be("scripted");
    }

    [Fact]
    public async Task Complete_ReportsUsage_AndEstimatesCostFromTheConfiguredPrices()
    {
        var script = new RecordingScript(_ => ScriptedReply.Say("Hello there."));

        var response = await Gateway(script).CompleteAsync(Request(new ModelMessage(ModelRole.User, "Hello")),
            CancellationToken.None);

        response.Text.Should().Be("Hello there.");
        response.ToolCalls.Should().BeEmpty();
        response.InputTokens.Should().BeGreaterThan(0);
        response.OutputTokens.Should().BeGreaterThan(0);
        response.EstimatedCost.Should().Be(response.InputTokens * 2m + response.OutputTokens * 4m);
    }

    [Fact]
    public async Task Complete_ReplaysEarlierCallsAndResults_ToTheModel()
    {
        var script = new RecordingScript(_ => ScriptedReply.Say("Done."));

        await Gateway(script).CompleteAsync(Request(
            new ModelMessage(ModelRole.User, "Say hi twice"),
            new ModelMessage(ModelRole.Assistant, ToolCalls: [new ModelToolCall("call-1", "test.echo", """{"text":"hi"}""")]),
            new ModelMessage(ModelRole.Tool, ToolResults: [new ModelToolResult("call-1", "test.echo", """{"text":"hihi"}""")])),
            CancellationToken.None);

        var turn = script.Turns.Should().ContainSingle().Subject;
        turn.AgentKey.Should().Be("test.assistant");
        turn.Prompt.Should().Be("Say hi twice");
        turn.TurnNumber.Should().Be(2);
        var result = turn.ToolResults.Should().ContainSingle().Subject;
        result.Tool.Should().Be("test.echo");
        JsonDocument.Parse(result.ResultJson).RootElement.GetProperty("text").GetString().Should().Be("hihi");
    }

    [Fact]
    public async Task Complete_ForAnAgentWithoutAScript_AnswersWithAnExplanation_InsteadOfFailing()
    {
        var script = new RecordingScript(_ => ScriptedReply.Say("unused"));
        var request = Request(new ModelMessage(ModelRole.User, "Hello")) with { AgentKey = "other.agent" };

        var response = await Gateway(script).CompleteAsync(request, CancellationToken.None);

        response.Text.Should().Contain("other.agent");
        script.Turns.Should().BeEmpty();
    }

    [Fact]
    public async Task Complete_ForAnUnknownModelKey_Throws()
    {
        var script = new RecordingScript(_ => ScriptedReply.Say("x"));
        var request = Request(new ModelMessage(ModelRole.User, "Hello")) with { ModelKey = "missing" };

        var act = () => Gateway(script).CompleteAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*missing*");
    }

    // ----- transcript ----------------------------------------------------------------------------------------------

    private static readonly DateTime Now = new(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);

    private static AgentRun RunningRun(string? input = null)
    {
        var run = AgentRun.Start("test.assistant", 1, "default", AgentAutonomy.Execute, AgentTriggerKind.User, Guid.NewGuid(),
            "Summarize", input, new AgentRunBudget(10, 10, 10_000, 60), "en");
        run.TryClaim("w", Now, TimeSpan.FromMinutes(5));
        return run;
    }

    [Fact]
    public void Transcript_IsRebuiltFromTheSteps_InOrder_WithEveryCallFollowedByItsResult()
    {
        var registry = TestAgents.Registry();
        var run = RunningRun("""{"id":7}""");
        var turn = run.RecordModelTurn("Let me look.", "m", "tool_calls", 1, 1, 0, 0, Now);
        var call = run.PlanToolCall(turn, "c1", "test.echo", """{"text":"a"}""");
        call.MarkExecuting(1, Now);
        call.Succeed("""{"text":"a"}""", 1, Now);

        var request = TranscriptBuilder.Build(registry.GetAgent("test.assistant"), run, [turn, call]);

        request.Instructions.Should().StartWith(TranscriptBuilder.Preamble).And.Contain("Help the user.");
        request.Messages.Select(m => m.Role).Should().Equal(ModelRole.User, ModelRole.Assistant, ModelRole.Tool);
        request.Messages[0].Text.Should().Contain("Summarize").And.Contain("""{"id":7}""");
        request.Messages[1].ToolCalls.Should().ContainSingle().Which.CallId.Should().Be("c1");
        request.Messages[2].ToolResults.Should().ContainSingle().Which.ResultJson.Should().Be("""{"text":"a"}""");
        request.Tools.Select(t => t.Name).Should().NotContain(AgentToolNames.SubmitResult);
    }

    [Fact]
    public void Transcript_OfATypedAgent_OffersSubmitResult_AndRemindsAModelThatAnsweredInProse()
    {
        var registry = TestAgents.Registry(TestAgents.Definition(outputType: typeof(Summary)));
        var run = RunningRun();
        var turn = run.RecordModelTurn("Here is my summary.", "m", "stop", 1, 1, 0, 0, Now);

        var request = TranscriptBuilder.Build(registry.GetAgent("test.assistant"), run, [turn]);

        request.Tools.Select(t => t.Name).Should().Contain(AgentToolNames.SubmitResult);
        request.Messages.Select(m => m.Role).Should().Equal(ModelRole.User, ModelRole.Assistant, ModelRole.User);
        request.Messages[2].Text.Should().Be(TranscriptBuilder.SubmitReminder);
    }

    [Fact]
    public void Transcript_WithAnOpenToolCall_IsRefused_SoAModelNeverSeesACallWithoutAResult()
    {
        var registry = TestAgents.Registry();
        var run = RunningRun();
        var turn = run.RecordModelTurn(null, "m", "tool_calls", 1, 1, 0, 0, Now);
        var call = run.PlanToolCall(turn, "c1", "test.echo", "{}");

        var act = () => TranscriptBuilder.Build(registry.GetAgent("test.assistant"), run, [turn, call]);

        act.Should().Throw<InvalidOperationException>();
    }

    // ----- options -------------------------------------------------------------------------------------------------

    [Fact]
    public void Options_WhenDisabled_AreNotValidated()
    {
        new AgenticOptionsValidator().Validate(null, new AgenticOptions { Enabled = false, DefaultModel = "nope" })
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Options_WithADefaultModelThatIsNotConfigured_FailStartup()
    {
        var settings = Settings();
        settings.DefaultModel = "nope";

        var result = new AgenticOptionsValidator().Validate(null, settings);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("DefaultModel");
    }

    [Fact]
    public void Options_WithAnAnthropicProviderWithoutAKey_FailStartup()
    {
        var settings = Settings();
        settings.Providers["claude"] = new AgentProviderOptions { Kind = AgentProviderKind.Anthropic };
        settings.Models["smart"] = new AgentModelOptions { Provider = "claude", ModelId = "claude-sonnet-5-5" };

        var result = new AgenticOptionsValidator().Validate(null, settings);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ApiKey");
    }

    [Fact]
    public void Options_WithASelfHostedOpenAiCompatibleServerWithoutAKey_AreValid()
    {
        var settings = Settings();
        settings.Providers["local"] = new AgentProviderOptions { Kind = AgentProviderKind.OpenAiCompatible, Endpoint = "http://localhost:11434/v1" };
        settings.Models["local"] = new AgentModelOptions { Provider = "local", ModelId = "llama" };

        new AgenticOptionsValidator().Validate(null, settings).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void RealProviderClients_AreBuiltFromConfiguration_WithoutCallingTheProvider()
    {
        var settings = Settings();
        settings.Providers["claude"] = new AgentProviderOptions { Kind = AgentProviderKind.Anthropic, ApiKey = "test-key" };
        settings.Providers["openai"] = new AgentProviderOptions { Kind = AgentProviderKind.OpenAiCompatible, ApiKey = "test-key" };
        settings.Providers["local"] = new AgentProviderOptions { Kind = AgentProviderKind.OpenAiCompatible, Endpoint = "http://localhost:11434/v1" };
        settings.Models["a"] = new AgentModelOptions { Provider = "claude", ModelId = "claude-sonnet-5-5" };
        settings.Models["b"] = new AgentModelOptions { Provider = "openai", ModelId = "gpt-test" };
        settings.Models["c"] = new AgentModelOptions { Provider = "local", ModelId = "llama" };
        using var factory = new AgentChatClientFactory(Microsoft.Extensions.Options.Options.Create(settings), [], NullLoggerFactory.Instance);

        factory.Get("a").Should().NotBeNull();
        factory.Get("b").Should().NotBeNull();
        factory.Get("c").Should().NotBeNull();
        factory.Get("a").Should().BeSameAs(factory.Get("a"));
    }
}
