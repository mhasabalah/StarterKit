using Agentic.Contracts.Agents;
using Agentic.Contracts.Conversations;
using Agentic.Contracts.Runs;
using Agentic.Domains.AgentRuns.Exceptions;
using Agentic.Domains.AgentRuns.Models;
using Agentic.Domains.Conversations.Exceptions;
using Agentic.Domains.Conversations.Models;
using Agentic.Runtime.Execution;
using Agentic.Runtime.Models;

namespace Agentic.Tests;

/// <summary>
/// Conversations: the aggregate's rules, and what an earlier turn becomes in the next turn's transcript (a pure
/// function, so every bound is tested here without a database).
/// </summary>
public class AgentConversationTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 10, 0, 0, DateTimeKind.Utc);

    private static ConversationTurn Turn(string prompt, string? answer, AgentRunStatus status = AgentRunStatus.Completed,
        string? outputJson = null, string? inputJson = null) =>
        new(prompt, inputJson, answer, outputJson, status);

    // ----- history in the transcript -------------------------------------------------------------------------------

    [Fact]
    public void History_PutsEachEarlierTurnBeforeTheNewMessage_AsAMessageAndItsAnswer_OldestFirst()
    {
        var history = TranscriptBuilder.History(
            [Turn("Where is shipment A-1?", "In Dubai."), Turn("And its VAT?", "50 USD.")], maxTurns: 20, maxCharacters: 10_000);

        var registry = TestAgents.Registry();
        var run = AgentRun.Start("test.assistant", 1, "default", AgentAutonomy.Execute, AgentTriggerKind.User, Guid.NewGuid(),
            "Is it cleared?", null, new AgentRunBudget(10, 10, 10_000, 60), "en", conversationId: Guid.NewGuid(),
            conversationTurn: 3);
        var request = TranscriptBuilder.Build(registry.GetAgent("test.assistant"), run, [], history);

        request.Messages.Select(m => (m.Role, m.Text)).Should().Equal(
            (ModelRole.User, "Where is shipment A-1?"), (ModelRole.Assistant, "In Dubai."),
            (ModelRole.User, "And its VAT?"), (ModelRole.Assistant, "50 USD."),
            (ModelRole.User, "Is it cleared?"));
        request.HistoryMessages.Should().Be(4, "the scripted provider and nothing else needs to know where the run begins");
    }

    [Fact]
    public void History_KeepsTheNewestTurns_WithinTheTurnLimit()
    {
        var turns = Enumerable.Range(1, 5).Select(i => Turn($"Q{i}", $"A{i}")).ToList();

        var history = TranscriptBuilder.History(turns, maxTurns: 2, maxCharacters: 10_000);

        history.Select(m => m.Text).Should().Equal("Q4", "A4", "Q5", "A5");
    }

    [Fact]
    public void History_DropsOlderTurnsWhole_WhenTheCharacterBudgetIsReached()
    {
        var turns = new[] { Turn(new string('x', 50), "old"), Turn("Q2", "A2"), Turn("Q3", "A3") };

        var history = TranscriptBuilder.History(turns, maxTurns: 20, maxCharacters: 20);

        history.Select(m => m.Text).Should().Equal("Q2", "A2", "Q3", "A3");
    }

    [Fact]
    public void History_LeavesOutTurnsWhoseContentWasErased()
    {
        var erased = new ConversationTurn(null, null, null, null, AgentRunStatus.Completed);

        var history = TranscriptBuilder.History([Turn("Q1", "A1"), erased, Turn("Q3", "A3")], 20, 10_000);

        history.Select(m => m.Text).Should().Equal("Q1", "A1", "Q3", "A3");
    }

    [Fact]
    public void History_SaysAnUnfinishedTurnDidNotFinish_InsteadOfRepeatingAPartialAnswer()
    {
        var history = TranscriptBuilder.History(
            [Turn("Q1", "half an answer", AgentRunStatus.Failed), Turn("Q2", null, AgentRunStatus.Cancelled)], 20, 10_000);

        history.Where(m => m.Role == ModelRole.Assistant).Select(m => m.Text)
            .Should().Equal(TranscriptBuilder.UnfinishedTurn, TranscriptBuilder.UnfinishedTurn);
    }

    [Fact]
    public void History_OfATypedTurn_RepeatsItsResult_AndTheInputOfEveryTurn()
    {
        var history = TranscriptBuilder.History(
            [Turn("Prepare it", null, outputJson: """{"ready":true}""", inputJson: """{"id":7}""")], 20, 10_000);

        history[0].Text.Should().Contain("Prepare it").And.Contain("""{"id":7}""");
        history[1].Text.Should().Contain("Result (JSON)").And.Contain("""{"ready":true}""");
    }

    [Fact]
    public void History_IsEmpty_WhenTheLimitsAreZero()
    {
        TranscriptBuilder.History([Turn("Q1", "A1")], maxTurns: 0, maxCharacters: 10_000).Should().BeEmpty();
        TranscriptBuilder.History([Turn("Q1", "A1")], maxTurns: 20, maxCharacters: 0).Should().BeEmpty();
    }

    // ----- the aggregate -------------------------------------------------------------------------------------------

    private static AgentConversation NewConversation(string? title = null) =>
        AgentConversation.Start("test.chat", Guid.NewGuid(), null, title, Now);

    [Fact]
    public void Start_TrimsTheTitle_AndOpensAnActiveConversationWithoutMessages()
    {
        var conversation = NewConversation("  Shipment questions  ");

        conversation.Status.Should().Be(AgentConversationStatus.Active);
        conversation.Title.Should().Be("Shipment questions");
        conversation.MessageCount.Should().Be(0);
        conversation.LastMessageAt.Should().Be(Now);
    }

    [Fact]
    public void Start_WithATitleOverTheLimit_IsRefused()
    {
        var act = () => NewConversation(new string('t', AgentConversation.TitleMaxLength + 1));

        act.Should().Throw<InvalidAgentConversationException>().Which.ErrorCode.Should().Be("title_too_long");
    }

    [Fact]
    public void RecordMessage_CountsMessages_UntilTheLimit_ThenRefusesWithConversationFull()
    {
        var conversation = NewConversation();
        conversation.RecordMessage(Now.AddMinutes(1), maxMessages: 2);
        conversation.RecordMessage(Now.AddMinutes(2), maxMessages: 2);

        var act = () => conversation.RecordMessage(Now.AddMinutes(3), maxMessages: 2);

        act.Should().Throw<AgentConversationLimitException>().Which.ErrorCode.Should().Be("conversation_full");
        conversation.MessageCount.Should().Be(2);
        conversation.LastMessageAt.Should().Be(Now.AddMinutes(2));
    }

    [Fact]
    public void RecordMessage_WithNoLimit_KeepsCounting()
    {
        var conversation = NewConversation();
        for (var i = 0; i < 5; i++)
            conversation.RecordMessage(Now, maxMessages: 0);

        conversation.MessageCount.Should().Be(5);
    }

    [Fact]
    public void Delete_ErasesTheTitle_AndNothingCanBeAddedOrRenamedAfterwards()
    {
        var conversation = NewConversation("Private title");

        conversation.Delete(Now);
        conversation.Delete(Now.AddHours(1)); // idempotent

        conversation.Status.Should().Be(AgentConversationStatus.Deleted);
        conversation.Title.Should().BeNull();
        conversation.DeletedAt.Should().Be(Now);
        FluentActions.Invoking(() => conversation.RecordMessage(Now, 10))
            .Should().Throw<InvalidAgentConversationException>().Which.ErrorCode.Should().Be("conversation_deleted");
        FluentActions.Invoking(() => conversation.Rename("Again"))
            .Should().Throw<InvalidAgentConversationException>();
    }

    [Fact]
    public void ATurnStartedTainted_NeedsApprovalForChanges_FromItsFirstStep()
    {
        var run = AgentRun.Start("test.assistant", 1, "default", AgentAutonomy.Execute, AgentTriggerKind.User, Guid.NewGuid(),
            "Go on", null, new AgentRunBudget(10, 10, 10_000, 60), "en", conversationId: Guid.NewGuid(), tainted: true,
            conversationTurn: 2);

        run.Tainted.Should().BeTrue();
        run.ChangesNeedApproval.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void AConversationTurn_NeedsBothItsConversationAndATurnNumberFromOne(bool inConversation, int? turn)
    {
        var act = () => AgentRun.Start("test.assistant", 1, "default", AgentAutonomy.Execute, AgentTriggerKind.User,
            Guid.NewGuid(), "Hi", null, new AgentRunBudget(10, 10, 10_000, 60), "en",
            conversationId: inConversation ? Guid.NewGuid() : null, conversationTurn: turn);

        act.Should().Throw<InvalidAgentRunStateException>();
    }
}
