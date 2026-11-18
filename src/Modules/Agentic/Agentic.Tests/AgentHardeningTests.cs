using System.Diagnostics;
using System.Text;
using Agentic.Contracts.Scripting;
using Agentic.Options;
using Agentic.Runtime.Models;
using Agentic.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agentic.Tests;

/// <summary>
/// Operational safety nets: canned answers never serve production, and prompts and tool content stay out of traces
/// unless an operator opts in.
/// </summary>
public class AgentHardeningTests
{
    private const string Secret = "SECRET-PROMPT-7f3a";

    private sealed class EchoScript : IAgentScript
    {
        public string AgentKey => "test.assistant";

        public ScriptedReply Next(ScriptedTurn turn) => ScriptedReply.Say($"Noted: {Secret}");
    }

    private static AgenticOptions Settings(AgentProviderKind kind, bool enabled = true, bool captureContent = false) => new()
    {
        Enabled = enabled,
        Models = { ["default"] = new AgentModelOptions { Provider = "main", ModelId = "model-1" } },
        Providers = { ["main"] = new AgentProviderOptions { Kind = kind, ApiKey = kind == AgentProviderKind.Scripted ? null : "CHANGE_ME" } },
        Telemetry = { CaptureContent = captureContent }
    };

    private static Task StartAsync(AgenticOptions settings, string environment)
    {
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(h => h.EnvironmentName).Returns(environment);
        return new AgenticStartupCheck(Microsoft.Extensions.Options.Options.Create(settings), host.Object)
            .StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task TheScriptedProvider_IsRefusedInProduction()
    {
        var start = () => StartAsync(Settings(AgentProviderKind.Scripted), Environments.Production);

        (await start.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Scripted");
    }

    [Theory]
    [InlineData(AgentProviderKind.Scripted, "Development", true)]
    [InlineData(AgentProviderKind.Scripted, "LoadTest", true)]
    [InlineData(AgentProviderKind.Scripted, "Production", false)] // agents off: nothing to refuse
    [InlineData(AgentProviderKind.OpenAiCompatible, "Production", true)]
    [InlineData(AgentProviderKind.Anthropic, "Production", true)]
    public async Task EveryOtherCombination_Starts(AgentProviderKind kind, string environment, bool enabled)
    {
        var start = () => StartAsync(Settings(kind, enabled), environment);

        await start.Should().NotThrowAsync();
    }

    /// <summary>Everything the module's spans recorded during one model request (tags and events), as text.</summary>
    private static async Task<string> TracedTextAsync(bool captureContent)
    {
        var recorded = new StringBuilder();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AgenticTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                foreach (var tag in activity.TagObjects)
                    recorded.Append(tag.Key).Append('=').Append(tag.Value).AppendLine();
                foreach (var activityEvent in activity.Events)
                {
                    recorded.AppendLine(activityEvent.Name);
                    foreach (var tag in activityEvent.Tags)
                        recorded.Append(tag.Key).Append('=').Append(tag.Value).AppendLine();
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        var options = Microsoft.Extensions.Options.Options.Create(Settings(AgentProviderKind.Scripted, captureContent: captureContent));
        var gateway = new ChatClientModelGateway(new AgentChatClientFactory(options, [new EchoScript()], NullLoggerFactory.Instance), options);
        await gateway.CompleteAsync(new ModelRequest("default", "test.assistant", "Be helpful.",
            [new ModelMessage(ModelRole.User, $"Remember {Secret}")], []), CancellationToken.None);

        return recorded.ToString();
    }

    [Fact]
    public async Task PromptsAndAnswers_StayOutOfTraces_ByDefault()
    {
        var traced = await TracedTextAsync(captureContent: false);

        traced.Should().Contain("gen_ai", "the model request is traced");
        traced.Should().NotContain(Secret);
    }

    [Fact]
    public async Task CaptureContent_PutsThemInTheTrace_SoTheDefaultIsAChoice()
    {
        var traced = await TracedTextAsync(captureContent: true);

        traced.Should().Contain(Secret);
    }
}
