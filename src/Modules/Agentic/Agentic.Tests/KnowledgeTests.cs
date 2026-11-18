using System.Text;
using Agentic.Contracts.Agents;
using Agentic.Contracts.Knowledge;
using Agentic.Contracts.Memory;
using Agentic.Domains.Knowledge.Models;
using Agentic.Domains.Memory.Models;
using Agentic.Options;
using Agentic.Runtime.Knowledge;
using Agentic.Runtime.Tools;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Agentic.Tests;

/// <summary>Knowledge and memory without a database: extraction, chunking, embeddings, the document and memory rules.</summary>
public class KnowledgeTests
{
    private static readonly KnowledgeOptions Options = new();

    // ----- chunking ---------------------------------------------------------------------------------------------

    [Fact]
    public void Chunking_IsDeterministic_CoversTheText_AndRespectsTheSize()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 40).Select(i =>
            $"Paragraph {i}. The shipment {i} needs a commercial invoice and a packing list before clearance."));

        var first = KnowledgeChunker.Split(text, 300, 50);
        var second = KnowledgeChunker.Split(text, 300, 50);

        first.Should().Equal(second);
        first.Should().OnlyContain(c => c.Content.Length <= 300 && c.Content.Length > 0);
        first.Select(c => c.StartOffset).Should().BeInAscendingOrder();
        first[0].StartOffset.Should().Be(0);
        // Every paragraph number appears in some passage: nothing is lost between passages.
        Enumerable.Range(1, 40).Should().OnlyContain(i => first.Any(c => c.Content.Contains($"Paragraph {i}.")));
        // Each passage really is the text at its offset.
        first.Should().OnlyContain(c => text.Substring(c.StartOffset, c.Content.Length) == c.Content);
    }

    [Fact]
    public void Chunking_PrefersParagraphBoundaries()
    {
        var text = new string('a', 150) + "\n\n" + new string('b', 150);

        var chunks = KnowledgeChunker.Split(text, 200, 20);

        chunks[0].Content.Should().Be(new string('a', 150));
        chunks.Should().Contain(c => c.Content == new string('b', 150));
    }

    [Fact]
    public void Chunking_AlwaysMovesForward_EvenWithoutAnyBoundary()
    {
        var text = new string('x', 1000);

        var chunks = KnowledgeChunker.Split(text, 200, 50);

        chunks.Should().NotBeEmpty();
        chunks.Sum(c => c.Content.Length).Should().BeGreaterThanOrEqualTo(1000);
        chunks[^1].StartOffset.Should().BeGreaterThan(chunks[0].StartOffset);
    }

    [Fact]
    public void Chunking_RefusesAnOverlapAsLargeAsTheSize() =>
        FluentActions.Invoking(() => KnowledgeChunker.Split("text", 100, 100)).Should().Throw<ArgumentOutOfRangeException>();

    // ----- extraction -------------------------------------------------------------------------------------------

    [Fact]
    public async Task PlainText_IsNormalized()
    {
        var text = await ExtractAsync("notes.txt", "text/plain", "Line one   \r\n\r\n\r\n\r\nLine two\r\n");

        text.Should().Be("Line one\n\nLine two");
    }

    [Fact]
    public async Task Html_LosesScriptsStylesAndTags_ButKeepsTheText()
    {
        var text = await ExtractAsync("page.html", "text/html",
            "<html><head><style>p{color:red}</style><script>alert('x')</script></head>" +
            "<body><p>Customs &amp; duties</p><div>Invoice</div></body></html>");

        text.Should().Contain("Customs & duties").And.Contain("Invoice");
        text.Should().NotContain("alert").And.NotContain("color").And.NotContain("<");
    }

    [Fact]
    public async Task Pdf_TextLayer_IsExtracted()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("Certificate of origin for shipment 42", 12, new PdfPoint(25, 700), font);

        await using var stream = new MemoryStream(builder.Build());
        var text = await KnowledgeTextExtractor.ExtractAsync(stream, "origin.pdf", "application/pdf", Options, CancellationToken.None);

        text.Should().Contain("Certificate of origin").And.Contain("42");
    }

    [Theory]
    [InlineData("image.png", "image/png", "unsupported_content_type")]
    [InlineData("broken.pdf", "application/pdf", "pdf_unreadable")]
    [InlineData("empty.txt", "text/plain", "no_text")]
    public async Task ContentThatCanNeverBeIndexed_FailsWithAStableCode(string file, string type, string code)
    {
        var content = file == "empty.txt" ? "   \n  " : "not really this type";

        var act = () => ExtractAsync(file, type, content);

        (await act.Should().ThrowAsync<KnowledgeIndexingException>()).Which.Code.Should().Be(code);
    }

    [Fact]
    public async Task AFileLargerThanTheLimit_IsRefusedWhileReading()
    {
        var options = new KnowledgeOptions { MaxDocumentBytes = 10 };

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("more than ten bytes of text"));
        var act = () => KnowledgeTextExtractor.ExtractAsync(stream, "a.txt", "text/plain", options, CancellationToken.None);

        (await act.Should().ThrowAsync<KnowledgeIndexingException>()).Which.Code.Should().Be("document_too_large");
    }

    [Fact]
    public void TextLongerThanTheLimit_IsRefused() =>
        FluentActions.Invoking(() => KnowledgeTextExtractor.Normalize("abcdef", 3))
            .Should().Throw<KnowledgeIndexingException>().Which.Code.Should().Be("text_too_long");

    private static async Task<string> ExtractAsync(string fileName, string contentType, string content)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return await KnowledgeTextExtractor.ExtractAsync(stream, fileName, contentType, Options, CancellationToken.None);
    }

    // ----- deterministic embeddings -----------------------------------------------------------------------------

    [Fact]
    public void HashingEmbeddings_AreDeterministic_Normalized_AndLexicallyMeaningful()
    {
        var generator = new HashingEmbeddingGenerator(256);

        var invoice = generator.Embed("commercial invoice for the shipment");
        var again = generator.Embed("Commercial INVOICE for the shipment!");
        var related = generator.Embed("the invoice of a shipment");
        var unrelated = generator.Embed("holiday schedule of the support team");

        invoice.Should().Equal(again);
        MathF.Sqrt(invoice.Sum(x => x * x)).Should().BeApproximately(1f, 1e-5f);
        Cosine(invoice, related).Should().BeGreaterThan(Cosine(invoice, unrelated));
    }

    [Fact]
    public void VectorLiterals_UseTheInvariantCulture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            KnowledgeStore.ToLiteral([0.5f, -1f, 0.25f]).Should().Be("[0.5,-1,0.25]");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    private static float Cosine(float[] a, float[] b) => a.Zip(b, (x, y) => x * y).Sum();

    // ----- the document -----------------------------------------------------------------------------------------

    private static readonly DateTime Now = new(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);

    private const string Model = "scripted:hashing:default";

    [Fact]
    public void TheSameContent_IsNotIndexedAgain_ButANewOneIsANewRevision_WhileTheOldOneStaysSearchable()
    {
        var document = KnowledgeDocument.Create("test.handbook", "doc-1", "Handbook", "Version one.", null, Now);
        document.BeginAttempt(1, Now).Should().BeTrue();
        document.MarkIndexed(1, 3, Model, Now);

        document.Replace("Handbook (renamed)", "Version one.", null, Model).Should().BeFalse();
        document.Title.Should().Be("Handbook (renamed)");
        document.Revision.Should().Be(1);
        document.Status.Should().Be(KnowledgeDocumentStatus.Ready);

        document.Replace("Handbook", "Version two.", null, Model).Should().BeTrue();
        document.Revision.Should().Be(2);
        document.Status.Should().Be(KnowledgeDocumentStatus.Pending);
        document.Attempts.Should().Be(0);
        document.IndexedRevision.Should().Be(1, "the indexed passages serve searches until the new ones replace them");
    }

    [Fact]
    public void ADocumentIndexedWithAnotherEmbeddingModel_IsIndexedAgain_WithTheSameContent()
    {
        var document = KnowledgeDocument.Create("test.handbook", "doc-1", "Handbook", "Text.", null, Now);
        document.BeginAttempt(1, Now);
        document.MarkIndexed(1, 1, "openaicompatible:old-model:default", Now);

        document.Replace("Handbook", "Text.", null, Model).Should().BeTrue();
        document.Revision.Should().Be(2);
    }

    [Fact]
    public void AFailedDocument_IsTriedAgain_WithTheSameContent_AndServesNothing()
    {
        var document = KnowledgeDocument.Create("test.handbook", "doc-1", "Handbook", null, Guid.NewGuid(), Now);
        document.BeginAttempt(1, Now);
        document.MarkFailed(1, "no_text");
        document.IndexedRevision.Should().BeNull();

        document.Replace("Handbook", null, document.AttachmentId, Model).Should().BeTrue();
        document.Revision.Should().Be(2);
        document.FailureCode.Should().BeNull();
    }

    [Fact]
    public void AJobForAnOldRevision_DoesNothing_AndCannotStoreItsResult()
    {
        var document = KnowledgeDocument.Create("test.handbook", "doc-1", "Handbook", "One.", null, Now);
        document.Replace("Handbook", "Two.", null, Model);

        document.BeginAttempt(1, Now).Should().BeFalse();
        FluentActions.Invoking(() => document.MarkIndexed(1, 1, "m", Now)).Should().Throw<InvalidOperationException>();
        document.BeginAttempt(2, Now.AddMinutes(30)).Should().BeTrue();
        document.Attempts.Should().Be(1);
        document.LastEnqueuedAt.Should().Be(Now.AddMinutes(30), "a working job is not stalled");
    }

    [Fact]
    public void ADocument_HasEitherTextOrAnAttachment() =>
        FluentActions.Invoking(() => KnowledgeDocument.Create("test.handbook", "doc-1", "T", "text", Guid.NewGuid(), Now))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void APendingDocument_NotQueuedForAWhile_IsStalled()
    {
        var document = KnowledgeDocument.Create("test.handbook", "doc-1", "Handbook", "One.", null, Now);
        document.MarkEnqueued(Now);

        document.IsStalled(Now.AddMinutes(5), TimeSpan.FromMinutes(15)).Should().BeFalse();
        document.IsStalled(Now.AddMinutes(16), TimeSpan.FromMinutes(15)).Should().BeTrue();
    }

    // ----- sources ----------------------------------------------------------------------------------------------

    [Fact]
    public void Sources_AreReadable_OnlyWithTheirPermission()
    {
        var registry = new KnowledgeSourceRegistry(
        [
            new KnowledgeSourceDefinition { Key = "test.handbook", Name = new("Handbook", "دليل"), AllowAnyMember = true },
            new KnowledgeSourceDefinition { Key = "test.contracts", Name = new("Contracts", "عقود"), RequiredPermission = "contracts.read" }
        ]);

        registry.ReadableBy(TestPermissions.Of()).Should().Equal("test.handbook");
        registry.ReadableBy(TestPermissions.Of("contracts.read")).Should().BeEquivalentTo(["test.handbook", "test.contracts"]);
    }

    [Theory]
    [InlineData("test.handbook", null, false, "exactly one")]
    [InlineData("test.handbook", "x.read", true, "exactly one")]
    [InlineData("Handbook", null, true, "must look like")]
    public void ASourceWithoutAClearAccessDecisionOrWithABadKey_StopsTheHost(string key, string? permission, bool anyMember,
        string message) =>
        FluentActions.Invoking(() => new KnowledgeSourceRegistry(
                [new KnowledgeSourceDefinition { Key = key, Name = new("S", "S"), RequiredPermission = permission, AllowAnyMember = anyMember }]))
            .Should().Throw<InvalidOperationException>().WithMessage($"*{message}*");

    [Fact]
    public void TwoSourcesWithTheSameKey_StopTheHost()
    {
        var source = new KnowledgeSourceDefinition { Key = "test.handbook", Name = new("H", "H"), AllowAnyMember = true };

        FluentActions.Invoking(() => new KnowledgeSourceRegistry([source, source]))
            .Should().Throw<InvalidOperationException>().WithMessage("*Two knowledge sources*");
    }

    // ----- options ----------------------------------------------------------------------------------------------

    [Fact]
    public void Knowledge_CannotUseAnthropicForEmbeddings()
    {
        var options = new AgenticOptions
        {
            Enabled = true,
            Models = { ["default"] = new AgentModelOptions { Provider = "claude", ModelId = "m" } },
            Providers = { ["claude"] = new AgentProviderOptions { Kind = AgentProviderKind.Anthropic, ApiKey = "CHANGE_ME" } },
            Knowledge = { Enabled = true, EmbeddingProvider = "claude", EmbeddingModelId = "e" }
        };

        var result = new AgenticOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("no embeddings API");
    }

    // ----- memory -----------------------------------------------------------------------------------------------

    [Fact]
    public void APersonalMemory_BelongsToItsAuthor_ATenantMemoryToNobody()
    {
        var author = Guid.NewGuid();

        var personal = AgentMemory.Write(AgentMemoryScope.User, author, "  Prefers sea freight.  ", Now, TimeSpan.FromDays(30));
        var tenant = AgentMemory.Write(AgentMemoryScope.Tenant, author, "Fiscal year starts in April.", Now, null);

        personal.OwnerUserId.Should().Be(author);
        personal.Content.Should().Be("Prefers sea freight.");
        personal.ExpiresAt.Should().Be(Now.AddDays(30));
        personal.Source.Should().Be(AgentMemorySource.Person);
        personal.IsVisibleTo(Guid.NewGuid()).Should().BeFalse();
        tenant.OwnerUserId.Should().BeNull();
        tenant.ExpiresAt.Should().BeNull();
        tenant.IsVisibleTo(Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void AMemoryWrittenByARun_RecordsTheRun()
    {
        var run = Guid.NewGuid();

        var memory = AgentMemory.Write(AgentMemoryScope.User, Guid.NewGuid(), "Ship to the Jebel Ali warehouse.", Now, null, run, "key");

        memory.Source.Should().Be(AgentMemorySource.Agent);
        memory.SourceRunId.Should().Be(run);
        memory.IdempotencyKey.Should().Be("key");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyMemory_IsRefused(string content) =>
        FluentActions.Invoking(() => AgentMemory.Write(AgentMemoryScope.User, Guid.NewGuid(), content, Now, null))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void ATooLongMemory_IsRefused() =>
        FluentActions.Invoking(() => AgentMemory.Write(AgentMemoryScope.User, Guid.NewGuid(),
                new string('m', AgentMemory.ContentMaxLength + 1), Now, null))
            .Should().Throw<ArgumentException>();

    // ----- a tool that always asks a person ---------------------------------------------------------------------

    [Fact]
    public void AToolThatRequiresApproval_AsksAPerson_EvenAtFullAutonomy_AndUntainted()
    {
        var registry = TestAgents.Registry(TestAgents.Definition(tools: ["test.remember"]));
        var agent = registry.GetAgent("test.assistant");

        ToolPolicy.Decide(agent, registry.FindTool("test.remember"), AgentAutonomy.Execute, false,
                TestPermissions.Of(TestPermissions.NotesWrite))
            .Should().Be(ToolDecision.RequireApproval);
    }

    [Fact]
    public void AToolThatRequiresApproval_IsStillRefused_WithoutItsPermission()
    {
        var registry = TestAgents.Registry(TestAgents.Definition(tools: ["test.remember"]));

        ToolPolicy.Decide(registry.GetAgent("test.assistant"), registry.FindTool("test.remember"), AgentAutonomy.Execute,
                false, TestPermissions.Of())
            .Should().Be(ToolDecision.Refuse(ToolPolicy.Forbidden));
    }
}
