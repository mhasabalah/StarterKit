using Agentic.Contracts.Conversations;
using Agentic.Contracts.Scripting;
using Agentic.Data;
using Agentic.Domains.AgentRuns.Services;
using Agentic.Domains.Conversations.Jobs;
using Agentic.Domains.Conversations.Services;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Entitlements;
using Shared.Core.MultiTenancy;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>
/// Conversation summaries on PostgreSQL (ADR-0031): scheduled after enough turns, written by the summary model, given
/// to the next turns instead of the turns they cover, charged to the plan, and erased with the turns' content.
/// </summary>
[Collection(AgenticPostgresCollection.Name)]
public class ConversationSummaryTests
{
    private readonly AgenticPostgresFixture _fx;

    public ConversationSummaryTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
        _fx.Options.Conversations.SummarizeAfterTurns = 3;
        _fx.Options.Conversations.KeepRecentTurns = 1;
    }

    private Task<Domains.Conversations.Models.AgentConversation> ConversationOfAsync(TenantInfo tenant, Guid id) =>
        _fx.InTenantAsync(tenant, sp => sp.GetRequiredService<AgenticDbContext>().Conversations.AsNoTracking()
            .SingleAsync(c => c.Id == id));

    private Task<bool> SummarizeAsync(TenantInfo tenant, Guid conversationId) =>
        _fx.InTenantAsync(tenant, sp => sp.GetRequiredService<SummarizeAgentConversationJob>()
            .ExecuteAsync(conversationId, CancellationToken.None));

    /// <summary>Starts a conversation and plays <paramref name="messages"/> as finished turns.</summary>
    private async Task<Guid> ConversationWithTurnsAsync(Guid user, params string[] messages)
    {
        var first = await _fx.StartConversationAsync(TenantA, messages[0], user);
        await _fx.RunAsync(TenantA, first.Run.Id);
        foreach (var message in messages.Skip(1))
        {
            var turn = await _fx.SendMessageAsync(TenantA, first.ConversationId, message, user);
            await _fx.RunAsync(TenantA, turn.Run.Id);
        }

        return first.ConversationId;
    }

    private int SummaryJobsFor(Guid conversationId) =>
        _fx.Jobs.For(conversationId).Count(j => j.Job.Type == typeof(SummarizeAgentConversationJob));

    [Fact]
    public async Task AfterEnoughTurns_ASummaryIsWritten_AndTheNextTurnGetsItInsteadOfTheTurnsItCovers()
    {
        var user = Guid.NewGuid();
        var seen = new List<ScriptedTurn>();
        _fx.Board.Next = turn =>
        {
            seen.Add(turn);
            return ScriptedReply.Say($"Answer to: {turn.Prompt}");
        };

        var id = await ConversationWithTurnsAsync(user, "Where is A-1?", "Where is B-2?", "Where is C-3?");
        SummaryJobsFor(id).Should().Be(0, "three finished turns are not more than SummarizeAfterTurns");
        var fourth = await _fx.SendMessageAsync(TenantA, id, "Where is D-4?", user);
        await _fx.RunAsync(TenantA, fourth.Run.Id);
        SummaryJobsFor(id).Should().Be(1);

        (await SummarizeAsync(TenantA, id)).Should().BeTrue();

        var conversation = await ConversationOfAsync(TenantA, id);
        conversation.SummaryThroughTurn.Should().Be(3, "the newest turn stays word for word");
        conversation.Summary.Should().Be(ConversationSummaryScript.Prefix + "Where is A-1? | Where is B-2? | Where is C-3?");
        conversation.SummaryInputTokens.Should().BePositive();
        _fx.Quota.Used(EntitlementKeys.AgentTokens).Should().BeGreaterThanOrEqualTo(
            conversation.SummaryInputTokens + conversation.SummaryOutputTokens);

        var fifth = await _fx.SendMessageAsync(TenantA, id, "And E-5?", user);
        await _fx.RunAsync(TenantA, fifth.Run.Id);

        var last = seen[^1];
        last.Summary.Should().Be(conversation.Summary);
        last.EarlierTurns.Select(t => t.UserMessage).Should().Equal("Where is D-4?");

        var detail = await _fx.SendAsync(TenantA, new GetAgentConversationByIdQuery(id), user);
        detail.Summary.Should().Be(conversation.Summary);
        detail.SummaryThroughTurn.Should().Be(3);
        detail.Messages.Should().HaveCount(5, "every turn is still listed");
    }

    [Fact]
    public async Task ASecondSummaryJob_ForTheSameTurns_WritesNothing_AndCostsNothing()
    {
        var id = await ConversationWithTurnsAsync(Guid.NewGuid(), "One", "Two", "Three", "Four");
        (await SummarizeAsync(TenantA, id)).Should().BeTrue();
        var used = _fx.Quota.Used(EntitlementKeys.AgentTokens);

        (await SummarizeAsync(TenantA, id)).Should().BeFalse();

        _fx.Quota.Used(EntitlementKeys.AgentTokens).Should().Be(used, "nothing needed a summary, so no model was called");
    }

    [Fact]
    public async Task ALaterSummary_BuildsOnThePreviousOne()
    {
        var user = Guid.NewGuid();
        var id = await ConversationWithTurnsAsync(user, "One", "Two", "Three", "Four");
        await SummarizeAsync(TenantA, id);

        foreach (var message in new[] { "Five", "Six", "Seven" })
        {
            var turn = await _fx.SendMessageAsync(TenantA, id, message, user);
            await _fx.RunAsync(TenantA, turn.Run.Id);
        }

        (await SummarizeAsync(TenantA, id)).Should().BeTrue();

        var conversation = await ConversationOfAsync(TenantA, id);
        conversation.SummaryThroughTurn.Should().Be(6);
        conversation.Summary.Should().Be(ConversationSummaryScript.Prefix + "One | Two | Three | Four | Five | Six");
    }

    [Fact]
    public async Task WhenThePlanHasNoTokensLeft_NoSummaryIsSaved()
    {
        var id = await ConversationWithTurnsAsync(Guid.NewGuid(), "One", "Two", "Three", "Four");
        _fx.Quota.Limits[EntitlementKeys.AgentTokens] = _fx.Quota.Used(EntitlementKeys.AgentTokens);

        (await SummarizeAsync(TenantA, id)).Should().BeFalse();

        (await ConversationOfAsync(TenantA, id)).Summary.Should().BeNull();
    }

    [Fact]
    public async Task ErasingTheContentOfASummarizedTurn_DropsTheSummary()
    {
        var id = await ConversationWithTurnsAsync(Guid.NewGuid(), "One", "Two", "Three", "Four");
        await SummarizeAsync(TenantA, id);
        var firstTurn = await _fx.InTenantAsync(TenantA, sp => sp.GetRequiredService<AgenticDbContext>().Runs
            .Where(r => r.ConversationId == id && r.ConversationTurn == 1).Select(r => r.Id).SingleAsync());

        await _fx.InTenantAsync(TenantA, sp => AgentRunContentEraser.EraseAsync(sp.GetRequiredService<AgenticDbContext>(),
            [firstTurn], DateTime.UtcNow, CancellationToken.None));

        var conversation = await ConversationOfAsync(TenantA, id);
        conversation.Summary.Should().BeNull("a summary must not outlive the content it was written from");
        conversation.SummaryThroughTurn.Should().Be(0);
    }

    [Fact]
    public async Task DeletingTheConversation_ErasesTheSummary()
    {
        var user = Guid.NewGuid();
        var id = await ConversationWithTurnsAsync(user, "One", "Two", "Three", "Four");
        await SummarizeAsync(TenantA, id);

        await _fx.SendAsync(TenantA, new DeleteAgentConversationCommand(id), user);

        (await ConversationOfAsync(TenantA, id)).Summary.Should().BeNull();
        (await SummarizeAsync(TenantA, id)).Should().BeFalse("a deleted conversation is never summarized again");
    }

    [Fact]
    public async Task WithSummariesOff_NothingIsScheduledOrWritten()
    {
        _fx.Options.Conversations.SummarizeAfterTurns = 0;

        var id = await ConversationWithTurnsAsync(Guid.NewGuid(), "One", "Two", "Three", "Four", "Five");

        SummaryJobsFor(id).Should().Be(0);
        (await SummarizeAsync(TenantA, id)).Should().BeFalse();
    }
}
