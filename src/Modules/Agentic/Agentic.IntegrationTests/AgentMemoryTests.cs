using System.Text.Json;
using Agentic.Contracts.Approvals;
using Agentic.Contracts.Memory;
using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Data;
using Agentic.Domains.AgentRuns.Consumers;
using Agentic.Domains.AgentRuns.Exceptions;
using Agentic.Domains.AgentRuns.Jobs;
using Agentic.Domains.Memory.Features;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using UserDirectory.Contracts.Users.IntegrationEvents;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>
/// Memory and content lifecycle: people write memories, agents only propose them for approval; recall is a read tool;
/// account erasure and retention remove what people and models wrote and keep the facts.
/// </summary>
[Collection(AgenticPostgresCollection.Name)]
public class AgentMemoryTests
{
    private readonly AgenticPostgresFixture _fx;

    public AgentMemoryTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
    }

    private static string Fact() => "Prefers carrier " + Guid.NewGuid().ToString("N")[..8];

    private Task<AgentMemoryDto> WriteAsync(string content, AgentMemoryScope scope = AgentMemoryScope.User, Guid? user = null) =>
        _fx.SendAsync(TenantA, new CreateAgentMemoryCommand(content, scope), user);

    private async Task<List<AgentMemoryDto>> ListAsync(Guid? user = null, TenantInfo? tenant = null) =>
        (await _fx.SendAsync(tenant ?? TenantA, new GetAgentMemoriesQuery(PageSize: 100), user)).Result ?? [];

    // ----- people ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task APersonalMemory_IsSeenOnlyByItsOwner_ATenantMemoryByEveryone()
    {
        var (mine, shared) = (Fact(), Fact());
        await WriteAsync(mine, user: TestCaller.Alice);
        await WriteAsync(shared, AgentMemoryScope.Tenant, TestCaller.Alice);

        var alice = await ListAsync(TestCaller.Alice);
        var bob = await ListAsync(TestCaller.Bob);

        alice.Select(m => m.Content).Should().Contain([mine, shared]);
        bob.Select(m => m.Content).Should().Contain(shared).And.NotContain(mine);
        alice.Single(m => m.Content == mine).Source.Should().Be(AgentMemorySource.Person);
    }

    [Fact]
    public async Task ATenantMemory_NeedsAgentsManage()
    {
        _fx.Caller.DeniedTo.Add((TestCaller.Bob, TenantPermissions.AgentsManage));

        var act = () => WriteAsync(Fact(), AgentMemoryScope.Tenant, TestCaller.Bob);

        (await act.Should().ThrowAsync<AgentAccessDeniedException>()).Which.ErrorCode.Should().Be("memory_tenant_scope_forbidden");
    }

    [Fact]
    public async Task AnotherUsersMemory_CannotBeDeleted_AndLooksLikeItDoesNotExist()
    {
        var memory = await WriteAsync(Fact(), user: TestCaller.Alice);

        var asBob = () => _fx.SendAsync(TenantA, new DeleteAgentMemoryCommand(memory.Id), TestCaller.Bob);
        await asBob.Should().ThrowAsync<AgentMemoryNotFoundException>();

        (await _fx.SendAsync(TenantA, new DeleteAgentMemoryCommand(memory.Id), TestCaller.Alice)).Should().BeTrue();
        (await ListAsync(TestCaller.Alice)).Should().NotContain(m => m.Id == memory.Id);
    }

    [Fact]
    public async Task AMemoryOfAnotherTenant_DoesNotExist()
    {
        var memory = await WriteAsync(Fact(), user: TestCaller.Alice);

        var act = () => _fx.SendAsync(TenantB, new DeleteAgentMemoryCommand(memory.Id), TestCaller.Alice);

        await act.Should().ThrowAsync<AgentMemoryNotFoundException>();
        (await ListAsync(TestCaller.Alice, TenantB)).Should().NotContain(m => m.Id == memory.Id);
    }

    [Fact]
    public async Task ThePersonalLimit_IsEnforced()
    {
        var user = Guid.NewGuid();
        _fx.Options.Memory.MaxPerUser = 2;
        await WriteAsync(Fact(), user: user);
        await WriteAsync(Fact(), user: user);

        var act = () => WriteAsync(Fact(), user: user);

        (await act.Should().ThrowAsync<AgentMemoryLimitException>()).Which.ErrorCode.Should().Be("memory_limit_reached");
    }

    // ----- agents ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Recall_ReturnsTheUsersAndTheTenantsMemories_AndTaintsTheRun()
    {
        var (alices, bobs) = (Fact(), Fact());
        await WriteAsync(alices, user: TestCaller.Alice);
        await WriteAsync(bobs, user: TestCaller.Bob);
        _fx.Board.Play(ScriptedReply.Call("memory.recall", "{}"), ScriptedReply.Say("Noted."));

        var run = await _fx.StartAsync(TenantA, TestAgents.Researcher, user: TestCaller.Alice);
        await _fx.RunAsync(TenantA, run.Id);

        var step = (await _fx.StepsOfAsync(TenantA, run.Id)).Single(s => s.ToolName == "memory.recall");
        var recalled = JsonDocument.Parse(step.ResultJson!).RootElement.GetProperty("memories").EnumerateArray()
            .Select(m => m.GetProperty("content").GetString()).ToList();
        recalled.Should().Contain(alices).And.NotContain(bobs);
        (await _fx.RunOfAsync(TenantA, run.Id)).Tainted.Should().BeTrue();
    }

    [Fact]
    public async Task Remember_AlwaysWaitsForAPerson_ThenWritesAPersonalMemory_Once()
    {
        var fact = Fact();
        _fx.Board.Play(
            ScriptedReply.Call("memory.remember", JsonSerializer.Serialize(new { content = fact })),
            ScriptedReply.Say("I will remember that."));

        var run = await _fx.StartAsync(TenantA, TestAgents.Researcher, user: TestCaller.Alice);
        await _fx.RunAsync(TenantA, run.Id);

        (await _fx.RunOfAsync(TenantA, run.Id)).Status.Should().Be(AgentRunStatus.AwaitingApproval,
            "the run is untainted and its autonomy allows the tool, yet a memory needs a person");
        (await ListAsync(TestCaller.Alice)).Should().NotContain(m => m.Content == fact);

        await _fx.ApproveAndRunAsync(TenantA, run.Id);
        await _fx.RunAsync(TenantA, run.Id); // a duplicate delivery after the run finished

        (await _fx.RunOfAsync(TenantA, run.Id)).Status.Should().Be(AgentRunStatus.Completed);
        var memory = (await ListAsync(TestCaller.Alice)).Should().ContainSingle(m => m.Content == fact).Which;
        memory.Scope.Should().Be(AgentMemoryScope.User);
        memory.OwnerUserId.Should().Be(TestCaller.Alice);
        memory.Source.Should().Be(AgentMemorySource.Agent);
        memory.SourceRunId.Should().Be(run.Id);
        (await ListAsync(TestCaller.Bob)).Should().NotContain(m => m.Content == fact);
    }

    [Fact]
    public async Task Remember_IsRefused_WhenTheUserMayNotStartRuns_EvenAfterApproval()
    {
        _fx.Board.Play(
            ScriptedReply.Call("memory.remember", JsonSerializer.Serialize(new { content = Fact() })),
            ScriptedReply.Say("Done."));
        var run = await _fx.StartAsync(TenantA, TestAgents.Researcher, user: TestCaller.Alice);
        await _fx.RunAsync(TenantA, run.Id);
        var approval = await _fx.ApprovalOfAsync(TenantA, run.Id);
        await _fx.SendAsync(TenantA, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest), TestCaller.Bob);

        _fx.Caller.DeniedTo.Add((TestCaller.Alice, TenantPermissions.AgentRunsStart));
        await _fx.RunAsync(TenantA, run.Id);

        var step = (await _fx.StepsOfAsync(TenantA, run.Id)).Single(s => s.ToolName == "memory.remember");
        step.ErrorCode.Should().Be("forbidden");
    }

    [Fact]
    public async Task AnExpiredMemory_IsNotRecalled_AndIsPurged()
    {
        var user = Guid.NewGuid();
        var fact = Fact();
        _fx.Options.Memory.RetentionDays = 1;
        try
        {
            await WriteAsync(fact, user: user);
        }
        finally
        {
            _fx.Options.Memory.RetentionDays = 365;
        }

        _fx.Clock.Advance(TimeSpan.FromDays(2));

        (await ListAsync(user)).Should().NotContain(m => m.Content == fact);
        await _fx.InTenantAsync(TenantA, sp => sp.GetRequiredService<PurgeAgentContentJob>().PurgeCurrentTenantAsync(null));
        (await _fx.AdminScalarAsync<long>($"select count(*) from agentic.agent_memories where owner_user_id = '{user}'")).Should().Be(0);
    }

    // ----- erasure and retention -----------------------------------------------------------------------------------

    [Fact]
    public async Task AccountErasure_DeletesPersonalMemories_CancelsOpenRuns_AndErasesRunContent_InEveryTenant()
    {
        var carol = Guid.NewGuid();
        var tenantFact = Fact();
        await WriteAsync(Fact(), user: carol);
        await WriteAsync(tenantFact, AgentMemoryScope.Tenant, carol);
        await _fx.SendAsync(TenantB, new CreateAgentMemoryCommand(Fact()), carol);

        _fx.Board.Play(ScriptedReply.Say("Secret answer for Carol."));
        var finished = await _fx.StartAsync(TenantA, prompt: "Carol's private question", user: carol);
        await _fx.RunAsync(TenantA, finished.Id);
        var queued = await _fx.StartAsync(TenantA, prompt: "Carol's queued question", user: carol);
        var alices = await _fx.StartAsync(TenantA, prompt: "Alice's question", user: TestCaller.Alice);

        var context = new Mock<ConsumeContext<UserDeletedIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(new UserDeletedIntegrationEvent { UserId = carol });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        await using (var scope = _fx.Services.CreateAsyncScope())
        {
            var consumer = ActivatorUtilities.CreateInstance<UserDeletedAgenticConsumer>(scope.ServiceProvider);
            await consumer.Consume(context.Object);
            await consumer.Consume(context.Object); // a redelivery finds nothing left
        }

        (await _fx.AdminScalarAsync<long>(
                $"select count(*) from agentic.agent_memories where owner_user_id = '{carol}' and scope = 'User'"))
            .Should().Be(0, "personal memories go, in both tenants");
        (await ListAsync(TestCaller.Alice)).Should().Contain(m => m.Content == tenantFact, "a tenant memory is the tenant's");

        var erased = await _fx.RunOfAsync(TenantA, finished.Id);
        erased.Prompt.Should().BeNull();
        erased.OutputText.Should().BeNull();
        erased.ContentErasedAt.Should().NotBeNull();
        erased.Status.Should().Be(AgentRunStatus.Completed, "the facts stay");
        (await _fx.StepsOfAsync(TenantA, finished.Id)).Should().OnlyContain(s => s.Text == null && s.ArgumentsJson == null);

        var cancelled = await _fx.RunOfAsync(TenantA, queued.Id);
        cancelled.Status.Should().Be(AgentRunStatus.Cancelled);
        cancelled.Prompt.Should().BeNull();

        var untouched = await _fx.RunOfAsync(TenantA, alices.Id);
        untouched.Prompt.Should().Be("Alice's question");
        untouched.Status.Should().Be(AgentRunStatus.Queued);
    }

    [Fact]
    public async Task AccountErasure_StopsARunNoWorkerHolds_AndRemovesTheUsersDecisionNotes()
    {
        var dave = Guid.NewGuid();
        // A run marked Running whose worker is gone (no lease): nobody would confirm a cancel.
        var orphan = await _fx.StartAsync(TenantA, prompt: "Dave's question", user: dave);
        await _fx.AdminExecAsync($"update agentic.agent_runs set status = 'Running', lease_owner = null, lease_until = null where id = '{orphan.Id}'");
        // Dave decided an approval of Alice's run with a note.
        _fx.Board.Play(ScriptedReply.Call(SendMailTool.Name, """{"title":"Invoice"}"""), ScriptedReply.Say("Sent."));
        var alices = await _fx.StartAsync(TenantA, user: TestCaller.Alice);
        await _fx.RunAsync(TenantA, alices.Id);
        var approval = await _fx.ApprovalOfAsync(TenantA, alices.Id);
        await _fx.SendAsync(TenantA, new RejectAgentActionCommand(approval.Id, "Dave thinks the amount is wrong"), dave);

        await _fx.InTenantAsync(TenantA, sp =>
            ActivatorUtilities.CreateInstance<UserDeletedAgenticConsumer>(sp).EraseCurrentTenantAsync(dave, CancellationToken.None));

        var run = await _fx.RunOfAsync(TenantA, orphan.Id);
        run.Status.Should().Be(AgentRunStatus.Cancelled);
        run.ContentErasedAt.Should().NotBeNull();
        (await _fx.ApprovalOfAsync(TenantA, alices.Id)).DecisionNote.Should().BeNull();
    }

    [Fact]
    public async Task Retention_ErasesTheContentOfOldFinishedRuns_Only()
    {
        _fx.Board.Play(ScriptedReply.Say("Old answer."));
        var old = await _fx.StartAsync(TenantA, prompt: "Old question");
        await _fx.RunAsync(TenantA, old.Id);
        _fx.Clock.Advance(TimeSpan.FromDays(_fx.Options.Content.RetentionDays + 1));
        _fx.Board.Play(ScriptedReply.Say("Recent answer."));
        var recent = await _fx.StartAsync(TenantA, prompt: "Recent question");
        await _fx.RunAsync(TenantA, recent.Id);
        var waiting = await _fx.StartAsync(TenantA, prompt: "Still queued");

        await _fx.InTenantAsync(TenantA, sp => sp.GetRequiredService<PurgeAgentContentJob>().PurgeCurrentTenantAsync(null));

        var oldRun = await _fx.RunOfAsync(TenantA, old.Id);
        oldRun.Prompt.Should().BeNull();
        oldRun.ContentErasedAt.Should().NotBeNull();
        oldRun.ModelTurns.Should().Be(1, "counters stay");
        (await _fx.RunOfAsync(TenantA, recent.Id)).Prompt.Should().Be("Recent question");
        (await _fx.RunOfAsync(TenantA, waiting.Id)).Prompt.Should().Be("Still queued");
    }

    [Fact]
    public async Task WithRetentionOff_NothingIsErased()
    {
        _fx.Options.Content.RetentionDays = 0;
        _fx.Board.Play(ScriptedReply.Say("Kept."));
        var run = await _fx.StartAsync(TenantA, prompt: "Keep me");
        await _fx.RunAsync(TenantA, run.Id);
        _fx.Clock.Advance(TimeSpan.FromDays(400));

        await _fx.InTenantAsync(TenantA, sp => sp.GetRequiredService<PurgeAgentContentJob>().PurgeCurrentTenantAsync(null));

        (await _fx.RunOfAsync(TenantA, run.Id)).Prompt.Should().Be("Keep me");
    }
}
