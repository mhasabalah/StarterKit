using System.Text.Json;
using Agentic.Contracts.Knowledge;
using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Data;
using Agentic.Domains.Knowledge;
using Agentic.Domains.Knowledge.Consumers;
using Agentic.Domains.Knowledge.Jobs;
using Agentic.Domains.Knowledge.Models;
using Attachment.Contracts.Attachments.IntegrationEvents;
using FluentValidation;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>
/// Knowledge on real PostgreSQL with pgvector: modules index documents, a background job turns them into passages with
/// vectors, and the <c>knowledge.search</c> tool returns only what the user a run acts for may read, inside their
/// tenant. Each test uses its own unique words, because the tests share one database.
/// </summary>
[Collection(AgenticPostgresCollection.Name)]
public class KnowledgeTests
{
    private readonly AgenticPostgresFixture _fx;

    public KnowledgeTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
    }

    private static string Word() => "kw" + Guid.NewGuid().ToString("N")[..10];

    private static string Ref() => "doc-" + Guid.NewGuid().ToString("N");

    private static IndexKnowledgeDocumentCommand Text(string source, string reference, string text, string title = "Document") =>
        new(source, reference, title, Text: text);

    /// <summary>A run of the researcher that searches once; returns the run and the search result.</summary>
    private async Task<(Guid RunId, JsonElement Result)> SearchAsync(TenantInfo tenant, string query, Guid? user = null,
        IReadOnlyCollection<string>? apiKeyScopes = null, string[]? sources = null)
    {
        _fx.Board.Play(
            ScriptedReply.Call("knowledge.search", JsonSerializer.Serialize(new { query, sources, top = 8 })),
            ScriptedReply.Say("Here is what I found."));
        var run = await _fx.StartAsync(tenant, TestAgents.Researcher, "Look it up", user, apiKeyScopes);
        await _fx.RunAsync(tenant, run.Id);

        var step = (await _fx.StepsOfAsync(tenant, run.Id)).Single(s => s.ToolName == "knowledge.search");
        return (run.Id, JsonDocument.Parse(step.ResultJson!).RootElement.Clone());
    }

    private static List<string> Refs(JsonElement result) =>
        result.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("documentRef").GetString()!).ToList();

    private Task<long> ChunksAsync(Guid documentId, string where = "true") =>
        _fx.AdminScalarAsync<long>($"select count(*) from agentic.knowledge_chunks where document_id = '{documentId}' and {where}");

    // ----- indexing ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task IndexedText_BecomesSearchable_WithVectorsAndCitations_AndTaintsTheRun()
    {
        var word = Word();
        var reference = Ref();
        var text = string.Join("\n\n", Enumerable.Range(1, 6).Select(i =>
            $"Section {i}. Shipments to the free zone need form {word} and a commercial invoice before they leave the port."));

        var indexed = await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, text, "Free zone handbook"));

        indexed.Queued.Should().BeTrue();
        var document = await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference);
        document.Status.Should().Be(KnowledgeDocumentStatus.Ready);
        document.ChunkCount.Should().BeGreaterThan(1);
        document.EmbeddingModel.Should().Be("scripted:hashing:default");
        (await ChunksAsync(document.Id)).Should().Be(document.ChunkCount);
        (await ChunksAsync(document.Id, "embedding is null")).Should().Be(0, "every passage has a vector");
        (await ChunksAsync(document.Id, "vector_dims(embedding) = 256")).Should().Be(document.ChunkCount);

        var (runId, result) = await SearchAsync(TenantA, word);

        var hit = result.GetProperty("results").EnumerateArray().First(r => r.GetProperty("documentRef").GetString() == reference);
        hit.GetProperty("citation").GetString().Should().MatchRegex($"^test\\.handbook/{reference}#\\d+$");
        hit.GetProperty("title").GetString().Should().Be("Free zone handbook");
        hit.GetProperty("text").GetString().Should().Contain(word);
        (await _fx.RunOfAsync(TenantA, runId)).Tainted.Should().BeTrue("retrieved passages are content the platform does not control");
    }

    [Fact]
    public async Task TheSameContent_IsNotIndexedTwice_AndNewContent_ReplacesEveryPassage()
    {
        var (oldWord, newWord, reference) = (Word(), Word(), Ref());
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Old rule {oldWord}."));

        var again = await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Old rule {oldWord}."));
        var changed = await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"New rule {newWord}."));

        again.Queued.Should().BeFalse();
        again.Revision.Should().Be(1);
        changed.Revision.Should().Be(2);
        var document = await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference);
        document.Status.Should().Be(KnowledgeDocumentStatus.Ready);
        (await ChunksAsync(document.Id, $"content like '%{oldWord}%'")).Should().Be(0);
        (await ChunksAsync(document.Id, "revision = 2")).Should().Be(await ChunksAsync(document.Id));
    }

    [Fact]
    public async Task AJobForASupersededRevision_DoesNothing_AndOnlyTheLatestContentIsIndexed()
    {
        var (first, second, reference) = (Word(), Word(), Ref());
        var one = await _fx.SendWithoutRequestAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Draft {first}."));
        var two = await _fx.SendWithoutRequestAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Final {second}."));

        await _fx.RunIndexJobAsync(TenantA, one.DocumentId!.Value, one.Revision);
        (await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference)).Status.Should().Be(KnowledgeDocumentStatus.Pending);
        (await ChunksAsync(one.DocumentId.Value)).Should().Be(0);

        await _fx.RunIndexJobAsync(TenantA, two.DocumentId!.Value, two.Revision);
        await _fx.RunIndexJobAsync(TenantA, two.DocumentId.Value, two.Revision); // a duplicate delivery

        var document = await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference);
        document.Status.Should().Be(KnowledgeDocumentStatus.Ready);
        document.Attempts.Should().Be(1, "the duplicate found nothing to do");
        (await ChunksAsync(document.Id, $"content like '%{second}%'")).Should().BeGreaterThan(0);
        (await ChunksAsync(document.Id, $"content like '%{first}%'")).Should().Be(0);
    }

    [Fact]
    public async Task WhileANewRevisionIsIndexed_TheLastIndexedPassagesStaySearchable()
    {
        var (oldWord, newWord, reference) = (Word(), Word(), Ref());
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Current rule {oldWord}."));
        var pending = await _fx.SendWithoutRequestAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Next rule {newWord}."));

        var (_, meanwhile) = await SearchAsync(TenantA, oldWord);
        await _fx.RunIndexJobAsync(TenantA, pending.DocumentId!.Value, pending.Revision);
        var (_, afterwards) = await SearchAsync(TenantA, newWord);

        Refs(meanwhile).Should().Contain(reference);
        Refs(afterwards).Should().Contain(reference);
        var document = await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference);
        document.IndexedRevision.Should().Be(2);
        (await ChunksAsync(document.Id, $"content like '%{oldWord}%'")).Should().Be(0);
    }

    [Fact]
    public async Task ChangingTheEmbeddingModel_ReindexesADocumentSentAgain_WithTheSameContent()
    {
        var reference = Ref();
        var text = Text(KnowledgeWorld.Handbook, reference, $"Stable text {Word()}.");
        await _fx.IndexAsync(TenantA, text);

        _fx.Options.Knowledge.EmbeddingModelId = "hashing-v2";
        var again = await _fx.IndexAsync(TenantA, text);

        again.Queued.Should().BeTrue();
        (await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference)).EmbeddingModel.Should().Be("scripted:hashing-v2:default");
    }

    [Fact]
    public async Task ARevisionThatUsedUpItsAttempts_IsFailed_InsteadOfRunningAgain()
    {
        var reference = Ref();
        _fx.Options.Knowledge.MaxAttempts = 1;
        var result = await _fx.SendWithoutRequestAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Rule {Word()}."));
        // An attempt that never reached the job's error handling (a worker that died on the document).
        await _fx.AdminExecAsync($"update agentic.knowledge_documents set attempts = 1 where id = '{result.DocumentId}'");

        await _fx.RunIndexJobAsync(TenantA, result.DocumentId!.Value, result.Revision);

        var document = await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference);
        document.Status.Should().Be(KnowledgeDocumentStatus.Failed);
        document.FailureCode.Should().Be("indexing_failed");
        (await ChunksAsync(document.Id)).Should().Be(0);
    }

    [Fact]
    public async Task AnAttachment_IsReadThroughTheAttachmentPort_AndItsMarkupIsDropped()
    {
        var word = Word();
        var file = _fx.Knowledge.AddFile(TenantA, "policy.html", "text/html",
            $"<html><script>steal()</script><body><h1>Returns</h1><p>Returns need code {word}.</p></body></html>");

        await _fx.IndexAsync(TenantA, new IndexKnowledgeDocumentCommand(KnowledgeWorld.Handbook, Ref(), "Returns", AttachmentId: file));

        var document = await _fx.InTenantAsync(TenantA, sp => sp.GetRequiredService<AgenticDbContext>().KnowledgeDocuments
            .AsNoTracking().SingleAsync(d => d.AttachmentId == file));
        document.Status.Should().Be(KnowledgeDocumentStatus.Ready);
        (await ChunksAsync(document.Id, $"content like '%{word}%'")).Should().Be(1);
        (await ChunksAsync(document.Id, "content like '%steal%' or content like '%<%'")).Should().Be(0);
    }

    [Theory]
    [InlineData("scan.png", "image/png", "unsupported_content_type")]
    [InlineData(null, null, "attachment_not_found")]
    public async Task AnAttachmentThatCannotBeRead_FailsTheDocumentAtOnce(string? fileName, string? contentType, string code)
    {
        var file = fileName is null ? Guid.NewGuid() : _fx.Knowledge.AddFile(TenantA, fileName, contentType!, "binary");
        var reference = Ref();

        await _fx.IndexAsync(TenantA, new IndexKnowledgeDocumentCommand(KnowledgeWorld.Handbook, reference, "Scan", AttachmentId: file));

        var document = await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference);
        document.Status.Should().Be(KnowledgeDocumentStatus.Failed);
        document.FailureCode.Should().Be(code);
        _fx.Jobs.For(document.Id).Where(j => j.IsScheduled).Should().BeEmpty("content that can never be indexed is not retried");
    }

    [Fact]
    public async Task AnotherTenantsAttachment_CannotBeIndexed()
    {
        var file = _fx.Knowledge.AddFile(TenantB, "secret.txt", "text/plain", "Globex salaries.");
        var reference = Ref();

        await _fx.IndexAsync(TenantA, new IndexKnowledgeDocumentCommand(KnowledgeWorld.Handbook, reference, "Steal", AttachmentId: file));

        (await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference)).FailureCode.Should().Be("attachment_not_found");
    }

    [Fact]
    public async Task AProviderOutage_IsRetriedWithBackoff_AndTheDocumentIsIndexedWhenItRecovers()
    {
        var reference = Ref();
        _fx.Knowledge.EmbeddingFailures = 1;

        var result = await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Rule {Word()}."));

        var document = await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference);
        document.Status.Should().Be(KnowledgeDocumentStatus.Pending);
        document.Attempts.Should().Be(1);
        _fx.Jobs.For(document.Id).Should().Contain(j => j.IsScheduled, "the next attempt is scheduled with backoff");

        await _fx.RunIndexJobAsync(TenantA, document.Id, result.Revision);

        (await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference)).Status.Should().Be(KnowledgeDocumentStatus.Ready);
    }

    [Fact]
    public async Task AProviderThatStaysDown_FailsTheDocument_AfterTheConfiguredAttempts()
    {
        var reference = Ref();
        _fx.Options.Knowledge.MaxAttempts = 2;
        _fx.Knowledge.EmbeddingFailures = 100;

        var result = await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Rule {Word()}."));
        await _fx.RunIndexJobAsync(TenantA, result.DocumentId!.Value, result.Revision);

        var document = await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference);
        document.Status.Should().Be(KnowledgeDocumentStatus.Failed);
        document.FailureCode.Should().Be("indexing_failed");
        document.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task ADocumentWhoseJobWasLost_IsQueuedAgainByTheSweep()
    {
        var result = await _fx.SendWithoutRequestAsync(TenantA, Text(KnowledgeWorld.Handbook, Ref(), $"Rule {Word()}."));
        var queued = _fx.Jobs.For(result.DocumentId!.Value).Count;

        _fx.Clock.Advance(TimeSpan.FromMinutes(20));
        var requeued = await _fx.InTenantAsync(TenantA,
            sp => sp.GetRequiredService<RequeueStalledKnowledgeDocumentsJob>().RequeueCurrentTenantAsync(null));

        requeued.Should().BeGreaterThan(0);
        _fx.Jobs.For(result.DocumentId.Value).Should().HaveCount(queued + 1);
    }

    [Fact]
    public async Task IndexingAnUnknownSource_IsRefused_AndInvalidContentIsAValidationError()
    {
        var unknown = () => _fx.SendWithoutRequestAsync(TenantA, Text("test.unknown", Ref(), "Text."));
        var both = () => _fx.SendWithoutRequestAsync(TenantA,
            new IndexKnowledgeDocumentCommand(KnowledgeWorld.Handbook, Ref(), "Both", "Text.", Guid.NewGuid()));

        (await unknown.Should().ThrowAsync<KnowledgeSourceNotFoundException>()).Which.ErrorCode.Should().Be("knowledge_source_not_found");
        await both.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task WhileKnowledgeIsOff_IndexingDoesNothing()
    {
        _fx.Options.Knowledge.Enabled = false;
        var reference = Ref();

        var result = await _fx.SendWithoutRequestAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, "Text."));

        result.Queued.Should().BeFalse();
        result.DocumentId.Should().BeNull();
        (await _fx.AdminScalarAsync<long>($"select count(*) from agentic.knowledge_documents where external_ref = '{reference}'"))
            .Should().Be(0);
    }

    // ----- search: isolation and access -------------------------------------------------------------------------

    [Fact]
    public async Task Search_NeverLeavesTheTenant()
    {
        var word = Word();
        var reference = Ref();
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Acme pricing {word}."));

        var (_, inB) = await SearchAsync(TenantB, word);
        var (_, inA) = await SearchAsync(TenantA, word);

        Refs(inB).Should().NotContain(reference);
        Refs(inA).Should().Contain(reference);
    }

    [Fact]
    public async Task AnotherTenantsPassages_AreInvisible_EvenToRawSql()
    {
        var reference = Ref();
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Acme only {Word()}."));
        var document = await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference);

        var visibleToB = await _fx.InTenantAsync(TenantB, sp => sp.GetRequiredService<AgenticDbContext>().Database
            .SqlQuery<int>($"select count(*)::int as \"Value\" from agentic.knowledge_chunks where document_id = {document.Id}")
            .SingleAsync());

        visibleToB.Should().Be(0, "row-level security applies under the explicit tenant filter of the search SQL");
        (await ChunksAsync(document.Id)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ASourceTheUserMayNotRead_ReturnsNothingFromIt_WhateverTheModelAsks()
    {
        var word = Word();
        var contract = Ref();
        var handbook = Ref();
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Contracts, contract, $"Contract terms {word}."));
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, handbook, $"Handbook terms {word}."));
        _fx.Caller.DeniedTo.Add((TestCaller.Bob, KnowledgeWorld.ContractsPermission));

        var (_, bob) = await SearchAsync(TenantA, word, TestCaller.Bob);
        var (_, bobAsksForContracts) = await SearchAsync(TenantA, word, TestCaller.Bob, sources: [KnowledgeWorld.Contracts]);
        var (_, alice) = await SearchAsync(TenantA, word, TestCaller.Alice);

        Refs(bob).Should().Contain(handbook).And.NotContain(contract);
        Refs(bobAsksForContracts).Should().BeEmpty("asking for a source can only narrow what the user may read");
        Refs(alice).Should().Contain(handbook).And.Contain(contract);
    }

    [Fact]
    public async Task AnApiKeyCeiling_NarrowsTheSources_EvenForAUserWhoHoldsThePermission()
    {
        var word = Word();
        var contract = Ref();
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Contracts, contract, $"Contract terms {word}."));

        var (_, result) = await SearchAsync(TenantA, word, TestCaller.Alice, [TenantPermissions.AgentRunsStart]);

        Refs(result).Should().NotContain(contract);
    }

    [Fact]
    public async Task TheOwningModulesDocumentPolicy_HidesADocument()
    {
        var word = Word();
        var (visible, hidden) = (Ref(), Ref());
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Contracts, visible, $"Visible {word}."));
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Contracts, hidden, $"Hidden {word}."));
        _fx.Knowledge.HiddenContracts[hidden] = true;

        var (_, result) = await SearchAsync(TenantA, word);

        Refs(result).Should().Contain(visible).And.NotContain(hidden);
    }

    [Fact]
    public async Task WithoutAQueryEmbedding_SearchFallsBackToKeywords()
    {
        var word = Word();
        var reference = Ref();
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Keyword only {word}."));
        _fx.Knowledge.EmbeddingFailures = 1;

        var (_, result) = await SearchAsync(TenantA, word);

        Refs(result).Should().Contain(reference);
    }

    [Fact]
    public async Task AFailedDocument_IsNotSearchable()
    {
        var word = Word();
        var reference = Ref();
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Old {word}."));
        _fx.Options.Knowledge.MaxAttempts = 1;
        _fx.Knowledge.EmbeddingFailures = 1;
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"New {word} text."));

        (await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference)).Status.Should().Be(KnowledgeDocumentStatus.Failed);
        var (_, result) = await SearchAsync(TenantA, word);

        Refs(result).Should().NotContain(reference);
    }

    [Fact]
    public async Task WhileKnowledgeIsOff_TheToolTellsTheModel_AndTheRunGoesOn()
    {
        _fx.Options.Knowledge.Enabled = false;

        var (runId, result) = await SearchAsync(TenantA, "anything");

        result.GetProperty("error").GetProperty("code").GetString().Should().Be("knowledge_unavailable");
        (await _fx.RunOfAsync(TenantA, runId)).Status.Should().Be(AgentRunStatus.Completed);
    }

    // ----- lifecycle ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ADeletedAttachment_TakesItsDocumentAndPassagesWithIt()
    {
        var file = _fx.Knowledge.AddFile(TenantA, "manual.txt", "text/plain", $"Manual {Word()}.");
        var reference = Ref();
        await _fx.IndexAsync(TenantA, new IndexKnowledgeDocumentCommand(KnowledgeWorld.Handbook, reference, "Manual", AttachmentId: file));
        var document = await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference);

        var context = new Mock<ConsumeContext<AttachmentDeletedIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(new AttachmentDeletedIntegrationEvent { AttachmentId = file });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        await _fx.InTenantAsync(TenantA, async sp =>
        {
            var consumer = ActivatorUtilities.CreateInstance<AttachmentDeletedKnowledgeConsumer>(sp);
            await consumer.Consume(context.Object);
            await consumer.Consume(context.Object); // a redelivery finds nothing
            return true;
        });

        (await _fx.AdminScalarAsync<long>($"select count(*) from agentic.knowledge_documents where id = '{document.Id}'")).Should().Be(0);
        (await ChunksAsync(document.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Remove_DeletesTheDocumentAndItsPassages_AndIsIdempotent()
    {
        var reference = Ref();
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, $"Remove me {Word()}."));
        var document = await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference);

        var first = await _fx.SendWithoutRequestAsync(TenantA, new RemoveKnowledgeDocumentCommand(KnowledgeWorld.Handbook, reference));
        var second = await _fx.SendWithoutRequestAsync(TenantA, new RemoveKnowledgeDocumentCommand(KnowledgeWorld.Handbook, reference));

        first.Should().BeTrue();
        second.Should().BeFalse();
        (await ChunksAsync(document.Id)).Should().Be(0);
    }

    [Fact]
    public async Task TheSameReference_InTwoTenants_IsTwoDocuments()
    {
        var reference = Ref();

        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Handbook, reference, "Acme text."));
        await _fx.IndexAsync(TenantB, Text(KnowledgeWorld.Handbook, reference, "Globex text."));
        await _fx.SendWithoutRequestAsync(TenantB, new RemoveKnowledgeDocumentCommand(KnowledgeWorld.Handbook, reference));

        (await _fx.DocumentOfAsync(TenantA, KnowledgeWorld.Handbook, reference)).Status.Should().Be(KnowledgeDocumentStatus.Ready);
    }

    [Fact]
    public async Task Operators_ListTheDocuments_WithoutTheirContent()
    {
        var reference = Ref();
        await _fx.IndexAsync(TenantA, Text(KnowledgeWorld.Contracts, reference, $"Listed {Word()}."));

        var page = await _fx.SendAsync(TenantA, new GetKnowledgeDocumentsQuery(KnowledgeWorld.Contracts, PageSize: 100));

        var dto = page.Result.Should().ContainSingle(d => d.ExternalRef == reference).Which;
        dto.Status.Should().Be(KnowledgeDocumentStatus.Ready);
        dto.ChunkCount.Should().BeGreaterThan(0);
        page.Result.Should().OnlyContain(d => d.SourceKey == KnowledgeWorld.Contracts);
    }
}
