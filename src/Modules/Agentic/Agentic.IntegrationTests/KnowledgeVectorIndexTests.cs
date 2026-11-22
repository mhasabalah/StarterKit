using System.Text.Json;
using Agentic.Contracts.Knowledge;
using Agentic.Contracts.Scripting;
using Agentic.Data;
using Agentic.Domains.Knowledge;
using Agentic.Runtime.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shared.Core.MultiTenancy;
using static Agentic.IntegrationTests.AgenticPostgresFixture;

namespace Agentic.IntegrationTests;

/// <summary>
/// The HNSW index of knowledge vectors (ADR-0032) on PostgreSQL with pgvector: built after the migrations for the
/// configured dimension, idempotent, rebuilt when an interrupted build left it invalid, used by the search query, and
/// returning what exact search returns, inside the tenant.
/// </summary>
[Collection(AgenticPostgresCollection.Name)]
public class KnowledgeVectorIndexTests
{
    private const int Dimensions = AgentChatClientFactoryDimensions;

    /// <summary>The hashing embeddings of the scripted provider (256 values).</summary>
    private const int AgentChatClientFactoryDimensions = Runtime.Models.AgentChatClientFactory.DefaultHashingDimensions;

    private static readonly string IndexName = new KnowledgeVectorIndex(Dimensions, 100).Name;

    private readonly AgenticPostgresFixture _fx;

    public KnowledgeVectorIndexTests(AgenticPostgresFixture fx)
    {
        _fx = fx;
        _fx.Reset();
        _fx.Options.Knowledge.Dimensions = Dimensions;
    }

    private static string Word() => "kw" + Guid.NewGuid().ToString("N")[..10];

    private static string Ref() => "doc-" + Guid.NewGuid().ToString("N");

    /// <summary>The step as the deploy-time migrator runs it: on the schema owner's connection.</summary>
    private async Task BuildIndexAsync()
    {
        _fx.Options.Knowledge.VectorIndex = KnowledgeVectorIndexKind.Hnsw;
        var options = new DbContextOptionsBuilder<AgenticDbContext>()
            .UseNpgsql(_fx.AdminConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var admin = new AgenticDbContext(options);
        var step = new KnowledgeVectorIndexStep(Microsoft.Extensions.Options.Options.Create(_fx.Options),
            NullLogger<KnowledgeVectorIndexStep>.Instance);
        await step.RunAsync(admin, CancellationToken.None);
    }

    private async Task<bool?> IndexIsValidAsync() =>
        await _fx.AdminScalarAsync<string>(
                $"select i.indisvalid::text from pg_index i join pg_class c on c.oid = i.indexrelid where c.relname = '{IndexName}'") switch
        {
            "true" => true,
            "false" => false,
            _ => null
        };

    private async Task<List<string>> SearchAsync(TenantInfo tenant, string query)
    {
        _fx.Board.Play(
            ScriptedReply.Call("knowledge.search", JsonSerializer.Serialize(new { query, top = 8 })),
            ScriptedReply.Say("Here is what I found."));
        var run = await _fx.StartAsync(tenant, TestAgents.Researcher, "Look it up");
        await _fx.RunAsync(tenant, run.Id);

        var step = (await _fx.StepsOfAsync(tenant, run.Id)).Single(s => s.ToolName == "knowledge.search");
        return JsonDocument.Parse(step.ResultJson!).RootElement.GetProperty("results").EnumerateArray()
            .Select(r => r.GetProperty("documentRef").GetString()!).ToList();
    }

    [Fact]
    public async Task TheIndex_IsBuiltForTheConfiguredDimension_AndBuildingItAgainChangesNothing()
    {
        await BuildIndexAsync();
        await BuildIndexAsync();

        (await IndexIsValidAsync()).Should().BeTrue();
        (await _fx.AdminScalarAsync<long>($"select count(*) from pg_indexes where indexname = '{IndexName}'")).Should().Be(1);
        var definition = await _fx.AdminScalarAsync<string>($"select indexdef from pg_indexes where indexname = '{IndexName}'");
        definition.Should().Contain("USING hnsw").And.Contain($"vector({Dimensions})").And.Contain("vector_cosine_ops")
            .And.Contain($"vector_dims(embedding) = {Dimensions}");
    }

    [Fact]
    public async Task AnInvalidIndex_LeftByAnInterruptedBuild_IsBuiltAgain()
    {
        await BuildIndexAsync();
        await _fx.AdminExecAsync($"update pg_index set indisvalid = false where indexrelid = 'agentic.{IndexName}'::regclass");
        (await IndexIsValidAsync()).Should().BeFalse();

        await BuildIndexAsync();

        (await IndexIsValidAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task WithoutTheHnswSetting_NoIndexIsBuilt()
    {
        await _fx.AdminExecAsync($"drop index if exists agentic.{IndexName}");
        var options = new DbContextOptionsBuilder<AgenticDbContext>().UseNpgsql(_fx.AdminConnectionString)
            .UseSnakeCaseNamingConvention().Options;
        await using var admin = new AgenticDbContext(options);

        await new KnowledgeVectorIndexStep(Microsoft.Extensions.Options.Options.Create(_fx.Options),
            NullLogger<KnowledgeVectorIndexStep>.Instance).RunAsync(admin, CancellationToken.None);

        (await IndexIsValidAsync()).Should().BeNull();
    }

    [Fact]
    public async Task TheSearchQuery_IsAnsweredFromTheIndex()
    {
        await BuildIndexAsync();
        var index = KnowledgeVectorIndex.From(_fx.Options)!;
        var vector = new HashingEmbeddingGenerator(Dimensions).Embed("free zone invoice");

        await using var connection = new NpgsqlConnection(_fx.AdminConnectionString);
        await connection.OpenAsync();
        await using (var off = new NpgsqlCommand("set enable_seqscan = off", connection))
            await off.ExecuteNonQueryAsync();
        await using var explain = new NpgsqlCommand("EXPLAIN " + KnowledgeStore.SearchSql(index), connection);
        explain.Parameters.AddWithValue("query", "free zone invoice");
        explain.Parameters.AddWithValue("tenant", TenantA.Id);
        explain.Parameters.AddWithValue("sources", new[] { KnowledgeWorld.Handbook });
        explain.Parameters.AddWithValue("candidates", 40);
        explain.Parameters.Add(new NpgsqlParameter("vector", NpgsqlTypes.NpgsqlDbType.Text) { Value = KnowledgeStore.ToLiteral(vector) });
        explain.Parameters.AddWithValue("model", "scripted:hashing:256");
        explain.Parameters.AddWithValue("k", KnowledgeStore.FusionK);
        explain.Parameters.AddWithValue("limit", 8);
        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                plan.Add(reader.GetString(0));
        }

        string.Join("\n", plan).Should().Contain(IndexName);
        KnowledgeStore.SearchSql(null).Should().NotContain("vector_dims", "exact search stays the default query");
    }

    [Fact]
    public async Task SearchWithTheIndex_FindsWhatExactSearchFinds_InsideTheTenant()
    {
        var (word, other) = (Word(), Word());
        var (first, second, third) = (Ref(), Ref(), Ref());
        await _fx.IndexAsync(TenantA, new IndexKnowledgeDocumentCommand(KnowledgeWorld.Handbook, first, "Free zone",
            Text: $"Shipments to the free zone need form {word} and an invoice."));
        await _fx.IndexAsync(TenantA, new IndexKnowledgeDocumentCommand(KnowledgeWorld.Handbook, second, "Port",
            Text: $"Port storage of form {word} goods is charged per day."));
        await _fx.IndexAsync(TenantA, new IndexKnowledgeDocumentCommand(KnowledgeWorld.Handbook, third, "Other",
            Text: $"Unrelated passage about {other} holidays."));
        await _fx.IndexAsync(TenantB, new IndexKnowledgeDocumentCommand(KnowledgeWorld.Handbook, Ref(), "Tenant B",
            Text: $"Tenant B also mentions form {word}."));

        var exact = await SearchAsync(TenantA, $"form {word}");
        await BuildIndexAsync();
        var indexed = await SearchAsync(TenantA, $"form {word}");
        var inB = await SearchAsync(TenantB, $"form {word}");

        indexed.Take(2).Should().BeEquivalentTo(exact.Take(2)).And.BeEquivalentTo([first, second]);
        inB.Should().NotContain([first, second, third], "the index never widens a search beyond its tenant");
    }
}
