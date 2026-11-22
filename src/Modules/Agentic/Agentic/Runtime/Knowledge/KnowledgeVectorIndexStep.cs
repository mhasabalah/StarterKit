using System.Globalization;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using Npgsql;
using Shared.Core.Data;

namespace Agentic.Runtime.Knowledge;

/// <summary>Where vector search looks (ADR-0032).</summary>
public enum KnowledgeVectorIndexKind
{
    /// <summary>Exact search: every vector of the tenant's sources is compared (fine up to tens of thousands of passages).</summary>
    None,

    /// <summary>An approximate HNSW index per configured dimension (large corpora).</summary>
    Hnsw
}

/// <summary>The HNSW index search uses: the column cast it was built on and its search breadth.</summary>
/// <param name="Dimensions">The vector length the index covers (<c>Agentic:Knowledge:Dimensions</c>).</param>
/// <param name="EfSearch">Candidates the index visits per query (<c>hnsw.ef_search</c>).</param>
public sealed record KnowledgeVectorIndex(int Dimensions, int EfSearch)
{
    /// <summary>pgvector indexes <c>vector</c> up to 2000 dimensions and <c>halfvec</c> up to 4000.</summary>
    public const int MaxVectorDimensions = 2_000;

    public const int MaxHalfVectorDimensions = 4_000;

    /// <summary><c>vector(n)</c> or <c>halfvec(n)</c>: the type the column is cast to, in the index and in the query.</summary>
    public string ColumnType => string.Create(CultureInfo.InvariantCulture,
        $"{(Dimensions <= MaxVectorDimensions ? "vector" : "halfvec")}({Dimensions})");

    public string OperatorClass => Dimensions <= MaxVectorDimensions ? "vector_cosine_ops" : "halfvec_cosine_ops";

    public string Name => string.Create(CultureInfo.InvariantCulture, $"ix_knowledge_chunks_embedding_hnsw_{Dimensions}");

    /// <summary>The configured index, or null for exact search.</summary>
    public static KnowledgeVectorIndex? From(AgenticOptions settings) =>
        settings.Knowledge is { VectorIndex: KnowledgeVectorIndexKind.Hnsw, Dimensions: { } dimensions } knowledge
            ? new KnowledgeVectorIndex(dimensions, knowledge.HnswEfSearch)
            : null;
}

/// <summary>
/// Builds the HNSW index of knowledge vectors after the Agentic migrations (ADR-0032), when
/// <c>Agentic:Knowledge:VectorIndex=Hnsw</c>. The <c>embedding</c> column is untyped (ADR-0028: the dimension belongs to
/// the embedding model, not to the schema), so the index is a partial expression index for the configured dimension:
/// <c>(embedding::vector(n)) vector_cosine_ops WHERE vector_dims(embedding) = n</c>. Built CONCURRENTLY, so writes go on
/// while it builds; an invalid index left by an interrupted build is dropped and built again. Idempotent.
/// </summary>
public sealed class KnowledgeVectorIndexStep(IOptions<AgenticOptions> options, ILogger<KnowledgeVectorIndexStep> logger)
    : IPostMigrationStep
{
    public Type DbContextType => typeof(AgenticDbContext);

    public async Task RunAsync(DbContext migratedContext, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.Knowledge.Enabled || KnowledgeVectorIndex.From(settings) is not { } index)
            return;

        var database = migratedContext.Database;
        var valid = await IsValidAsync(database, index.Name, cancellationToken);
        if (valid == false)
        {
            logger.LogWarning("Knowledge vector index {Index} is invalid (an interrupted build); it is built again", index.Name);
            var drop = "DROP INDEX CONCURRENTLY IF EXISTS agentic." + index.Name + ";";
            await database.ExecuteSqlRawAsync(drop, cancellationToken);
        }

        if (valid == true)
            return;

        var hnsw = settings.Knowledge;
        // Every value below is an integer from validated configuration: nothing here comes from a request.
        var sql = string.Create(CultureInfo.InvariantCulture,
            $"CREATE INDEX CONCURRENTLY IF NOT EXISTS {index.Name} ON agentic.knowledge_chunks " +
            $"USING hnsw ((embedding::{index.ColumnType}) {index.OperatorClass}) " +
            $"WITH (m = {hnsw.HnswM}, ef_construction = {hnsw.HnswEfConstruction}) " +
            $"WHERE vector_dims(embedding) = {index.Dimensions};");
        database.SetCommandTimeout(TimeSpan.FromHours(2)); // a large corpus takes a while; deploy time only
        await database.ExecuteSqlRawAsync(sql, cancellationToken);
        logger.LogInformation("Knowledge vector index {Index} is ready", index.Name);
    }

    /// <summary>True when the index exists and is valid, false when it exists but is invalid, null when it does not exist.</summary>
    private static async Task<bool?> IsValidAsync(DatabaseFacade database, string name, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)database.GetDbConnection();
        await database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT i.indisvalid
                FROM pg_index i
                JOIN pg_class c ON c.oid = i.indexrelid
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'agentic' AND c.relname = @name
                """, connection);
            command.Parameters.AddWithValue("name", name);
            return await command.ExecuteScalarAsync(cancellationToken) is bool valid ? valid : null;
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }
}
