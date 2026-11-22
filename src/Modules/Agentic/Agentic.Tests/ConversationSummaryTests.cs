using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Domains.Conversations.Exceptions;
using Agentic.Domains.Conversations.Jobs;
using Agentic.Domains.Conversations.Models;
using Agentic.Domains.Conversations.Services;
using Agentic.Options;
using Agentic.Runtime.Execution;
using Agentic.Runtime.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Agentic.Tests;

/// <summary>Conversation summaries (ADR-0031): the aggregate's rules and how a summary enters the history.</summary>
public class ConversationSummaryTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    private static AgentConversation WithMessages(int count)
    {
        var conversation = AgentConversation.Start("test.chat", Guid.NewGuid(), null, "Chat", Now);
        for (var i = 0; i < count; i++)
            conversation.RecordMessage(Now, 0);
        return conversation;
    }

    private static ConversationTurn Turn(string prompt, string answer) => new(prompt, null, answer, null, AgentRunStatus.Completed);

    [Fact]
    public void RecordSummary_CoversTheTurnsUpToItsTurn_AndAddsUpItsTokensAndCost()
    {
        var conversation = WithMessages(10);

        conversation.RecordSummary("  First summary.  ", 4, 4_000, 100, 20, 0.5m, Now).Should().BeTrue();
        conversation.RecordSummary("Second summary.", 7, 4_000, 50, 10, 0.25m, Now.AddMinutes(1)).Should().BeTrue();

        conversation.Summary.Should().Be("Second summary.");
        conversation.SummaryThroughTurn.Should().Be(7);
        conversation.SummaryUpdatedAt.Should().Be(Now.AddMinutes(1));
        conversation.SummaryInputTokens.Should().Be(150);
        conversation.SummaryOutputTokens.Should().Be(30);
        conversation.SummaryCost.Should().Be(0.75m);
    }

    [Fact]
    public void RecordSummary_OfTurnsAlreadyCovered_ChangesNothing()
    {
        var conversation = WithMessages(10);
        conversation.RecordSummary("Newer.", 6, 4_000, 1, 1, 0, Now);

        conversation.RecordSummary("Older.", 6, 4_000, 1, 1, 0, Now).Should().BeFalse();
        conversation.RecordSummary("Older still.", 3, 4_000, 1, 1, 0, Now).Should().BeFalse();

        conversation.Summary.Should().Be("Newer.");
        conversation.SummaryInputTokens.Should().Be(1);
    }

    [Fact]
    public void RecordSummary_IsCutToTheLimit()
    {
        var conversation = WithMessages(3);

        conversation.RecordSummary(new string('x', 500), 2, 200, 0, 0, 0, Now);

        conversation.Summary.Should().HaveLength(200);
    }

    [Theory]
    [InlineData("", 2, "summary_empty")]
    [InlineData("Summary.", 0, "summary_turn_invalid")]
    [InlineData("Summary.", 4, "summary_turn_invalid")]
    public void RecordSummary_WithoutTextOrOfATurnTheConversationDoesNotHave_IsRefused(string text, int turn, string code)
    {
        var conversation = WithMessages(3);

        var act = () => conversation.RecordSummary(text, turn, 4_000, 0, 0, 0, Now);

        act.Should().Throw<InvalidAgentConversationException>().Which.ErrorCode.Should().Be(code);
    }

    [Fact]
    public void Delete_ErasesTheSummary_AndASummaryCanNotBeAddedAfterwards()
    {
        var conversation = WithMessages(5);
        conversation.RecordSummary("Summary.", 3, 4_000, 0, 0, 0, Now);

        conversation.Delete(Now);
        var act = () => conversation.RecordSummary("Again.", 4, 4_000, 0, 0, 0, Now);

        conversation.Summary.Should().BeNull();
        conversation.SummaryThroughTurn.Should().Be(0);
        act.Should().Throw<InvalidAgentConversationException>().Which.ErrorCode.Should().Be("conversation_deleted");
    }

    // ----- the summary in the history ---------------------------------------------------------------------------

    [Fact]
    public void History_SendsTheSummaryFirst_AsAMarkedMessage_ThenTheTurnsAfterIt()
    {
        var history = TranscriptBuilder.History([Turn("And the VAT?", "50 USD.")], 20, 10_000, "The user asked about shipment A-1.");

        history.Select(m => m.Role).Should().Equal(ModelRole.User, ModelRole.Assistant, ModelRole.User, ModelRole.Assistant);
        history[0].Text.Should().StartWith(TranscriptBuilder.SummaryHeader).And.EndWith("The user asked about shipment A-1.");
        history[1].Text.Should().Be(TranscriptBuilder.SummaryAcknowledgement);
        history[2].Text.Should().Be("And the VAT?");
    }

    [Fact]
    public void History_CountsTheSummary_AgainstTheCharacterBudget()
    {
        var summary = new string('s', 300);
        var limit = TranscriptBuilder.SummaryHeader.Length + 2 + summary.Length + 30;

        var history = TranscriptBuilder.History([Turn("An older question here", "An older answer here"), Turn("New?", "Yes.")],
            20, limit, summary);

        history.Should().HaveCount(4, "the summary and only the newest turn fit");
        history[2].Text.Should().Be("New?");
    }

    [Fact]
    public void History_LeavesOutASummaryThatDoesNotFit_RatherThanCuttingIt()
    {
        var history = TranscriptBuilder.History([Turn("Q", "A")], 20, 50, new string('s', 500));

        history.Select(m => m.Text).Should().Equal("Q", "A");
    }

    [Fact]
    public async Task TheScriptedProvider_HandsTheSummaryToTheScript_ApartFromTheEarlierTurns()
    {
        ScriptedTurn? seen = null;
        var script = new CapturingScript(turn => seen = turn);
        var options = Microsoft.Extensions.Options.Options.Create(new AgenticOptions
        {
            Enabled = true,
            Models = { ["default"] = new AgentModelOptions { Provider = "scripted", ModelId = "scripted" } },
            Providers = { ["scripted"] = new AgentProviderOptions { Kind = AgentProviderKind.Scripted } }
        });
        var gateway = new ChatClientModelGateway(new AgentChatClientFactory(options, [script], NullLoggerFactory.Instance), options);
        var history = TranscriptBuilder.History([Turn("And the VAT?", "50 USD.")], 20, 10_000, "Shipment A-1 was discussed.");

        await gateway.CompleteAsync(new ModelRequest("default", "test.assistant", "Be helpful.",
            [.. history, new ModelMessage(ModelRole.User, "Is it cleared?")], [], history.Count), CancellationToken.None);

        seen!.Summary.Should().Be("Shipment A-1 was discussed.");
        seen.EarlierTurns.Should().Equal(new ScriptedHistoryTurn("And the VAT?", "50 USD."));
        seen.Prompt.Should().Be("Is it cleared?");
    }

    private sealed class CapturingScript(Action<ScriptedTurn> capture) : IAgentScript
    {
        public string AgentKey => "test.assistant";

        public ScriptedReply Next(ScriptedTurn turn)
        {
            capture(turn);
            return ScriptedReply.Say("ok");
        }
    }

    // ----- what the model is asked to summarize ------------------------------------------------------------------

    [Fact]
    public void TheSummaryRequest_HoldsThePreviousSummary_AndEachTurnsMessageAndAnswer_NeverToolResults()
    {
        var text = SummarizeAgentConversationJob.Transcript("Earlier: A-1.", [Turn("Where is B-2?", "In Jeddah.")]);

        text.Should().StartWith("Summary so far:\nEarlier: A-1.");
        text.Should().Contain("\nUser: Where is B-2?").And.Contain("\nAssistant: In Jeddah.");
    }

    [Fact]
    public void TheScriptedSummary_KeepsTheUserMessages_AndBuildsOnThePreviousSummary()
    {
        var script = new ConversationSummaryScript();
        var transcript = SummarizeAgentConversationJob.Transcript("Earlier the user asked: Where is A-1?",
            [Turn("Where is B-2?", "In Jeddah."), Turn("And C-3?", "In Riyadh.")]);

        var reply = script.Next(new ScriptedTurn(SummarizeAgentConversationJob.AgentKey, transcript, 1, []));

        reply.Text.Should().Be("Earlier the user asked: Where is A-1? | Where is B-2? | And C-3?");
    }

    // ----- configuration ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(-1, 8, 4_000, null)]
    [InlineData(10, 0, 4_000, null)]
    [InlineData(10, 10, 4_000, null)]
    [InlineData(10, 4, 100, null)]
    [InlineData(10, 4, 4_000, "missing")]
    public void Options_WithAnInvalidSummarySetting_FailStartup(int after, int keep, int maxCharacters, string? model)
    {
        var settings = new AgenticOptions
        {
            Enabled = true,
            Models = { ["default"] = new AgentModelOptions { Provider = "scripted", ModelId = "scripted" } },
            Providers = { ["scripted"] = new AgentProviderOptions { Kind = AgentProviderKind.Scripted } },
            Conversations = { SummarizeAfterTurns = after, KeepRecentTurns = keep, MaxSummaryCharacters = maxCharacters, SummaryModel = model }
        };

        new AgenticOptionsValidator().Validate(null, settings).Failed.Should().BeTrue();
    }

    [Fact]
    public void Options_WithSummariesOff_IgnoreTheOtherSummarySettings()
    {
        var settings = new AgenticOptions
        {
            Enabled = true,
            Models = { ["default"] = new AgentModelOptions { Provider = "scripted", ModelId = "scripted" } },
            Providers = { ["scripted"] = new AgentProviderOptions { Kind = AgentProviderKind.Scripted } },
            Conversations = { SummarizeAfterTurns = 0, KeepRecentTurns = 0, MaxSummaryCharacters = 0 }
        };

        new AgenticOptionsValidator().Validate(null, settings).Succeeded.Should().BeTrue();
    }
}
