using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Agentic.Runtime.Knowledge;

/// <summary>A passage found by <see cref="KnowledgeStore.SearchAsync"/>, before document-level filtering.</summary>
public sealed record KnowledgeHit(
    Guid ChunkId,
    Guid DocumentId,
    string SourceKey,
    string ExternalRef,
    string Title,
    int Ordinal,
    string Content,
    double Score);

/// <summary>
/// The SQL behind knowledge: the two columns the EF model does not map (<c>embedding vector</c>, <c>search_vector
/// tsvector</c>). Every statement runs on the context's own connection, so the tenant session variables of RLS apply,
/// AND names the tenant explicitly (raw SQL does not get EF's query filters).
/// </summary>
public static class KnowledgeStore
{
    /// <summary>Reciprocal rank fusion constant (the usual 60): a passage ranked well by either search scores well.</summary>
    public const int FusionK = 60;

    /// <summary>Writes the vectors of freshly inserted chunks (call inside the indexing transaction).</summary>
    public static Task SetEmbeddingsAsync(AgenticDbContext db, Guid tenantId, IReadOnlyList<Guid> chunkIds,
        IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
    {
        if (chunkIds.Count != vectors.Count)
            throw new ArgumentException("One vector per chunk is required.");
        var ids = chunkIds.ToArray();
        var literals = vectors.Select(ToLiteral).ToArray();
        return db.Database.ExecuteSqlAsync(
            $"""
             UPDATE agentic.knowledge_chunks AS c
             SET embedding = CAST(v.e AS vector)
             FROM unnest({ids}, {literals}) AS v(id, e)
             WHERE c.id = v.id AND c.tenant_id = {tenantId}
             """,
            cancellationToken);
    }

    /// <summary>
    /// Hybrid search inside one tenant: full-text (<c>websearch_to_tsquery('simple', ...)</c>, no stemming, so Arabic
    /// and English behave alike) and vector distance (cosine) over the indexed revision of the documents of
    /// <paramref name="sources"/>, each limited to <paramref name="candidates"/>, merged by reciprocal rank fusion.
    /// Vectors are compared only with vectors of the same <paramref name="embeddingModel"/>.
    /// <para>
    /// Vector distance is exact unless <paramref name="index"/> is given and the query vector has its length: then the
    /// nearest passages come from the HNSW index (ADR-0032), scanned iteratively (<c>hnsw.iterative_scan</c>) so the
    /// tenant and source filters still leave enough candidates.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(AgenticDbContext db, Guid tenantId, string query,
        float[]? queryVector, string embeddingModel, IReadOnlyCollection<string> sources, int candidates, int limit,
        CancellationToken cancellationToken, KnowledgeVectorIndex? index = null)
    {
        var useIndex = index is not null && queryVector is not null && queryVector.Length == index.Dimensions;
        var sql = SearchSql(useIndex ? index : null);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken); // interceptors set the tenant session for RLS
        NpgsqlTransaction? own = null;
        try
        {
            var transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
            if (useIndex)
            {
                // SET LOCAL needs a transaction: the settings never leak to the next user of the pooled connection.
                transaction ??= own = await connection.BeginTransactionAsync(cancellationToken);
                await using var settings = new NpgsqlCommand(string.Create(CultureInfo.InvariantCulture,
                        $"SET LOCAL hnsw.iterative_scan = relaxed_order; SET LOCAL hnsw.ef_search = {index!.EfSearch};"),
                    connection, transaction);
                await settings.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("query", query);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("sources", sources.ToArray());
            command.Parameters.AddWithValue("candidates", candidates);
            command.Parameters.Add(new NpgsqlParameter("vector", NpgsqlTypes.NpgsqlDbType.Text)
            {
                Value = queryVector is null ? DBNull.Value : ToLiteral(queryVector)
            });
            command.Parameters.AddWithValue("model", embeddingModel);
            command.Parameters.AddWithValue("k", FusionK);
            command.Parameters.AddWithValue("limit", limit);

            var hits = new List<KnowledgeHit>();
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    hits.Add(new KnowledgeHit(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                        reader.GetString(4), reader.GetInt32(5), reader.GetString(6),
                        Convert.ToDouble(reader.GetValue(7), CultureInfo.InvariantCulture)));
                }
            }

            if (own is not null)
                await own.CommitAsync(cancellationToken);
            return hits;
        }
        finally
        {
            if (own is not null)
                await own.DisposeAsync();
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>The search statement: exact vector distance, or the nearest passages from the HNSW index.</summary>
    internal static string SearchSql(KnowledgeVectorIndex? index) =>
        SearchTemplate.Replace("{vec}", index is null ? ExactVector : IndexedVector(index), StringComparison.Ordinal);

    private const string SearchTemplate =
        """
        WITH kw AS (
            SELECT c.id, row_number() OVER (ORDER BY ts_rank_cd(c.search_vector, q.tsq) DESC, c.id) AS rank
            FROM agentic.knowledge_chunks c
            JOIN agentic.knowledge_documents d ON d.id = c.document_id AND d.indexed_revision = c.revision
            CROSS JOIN (SELECT websearch_to_tsquery('simple', @query) AS tsq) q
            WHERE c.tenant_id = @tenant AND d.tenant_id = @tenant
              AND d.source_key = ANY(@sources) AND c.search_vector @@ q.tsq
            ORDER BY rank
            LIMIT @candidates
        ),
        vec AS (
        {vec}
        )
        SELECT c.id, d.id, d.source_key, d.external_ref, d.title, c.ordinal, c.content,
               COALESCE(1.0 / (@k + kw.rank), 0) + COALESCE(1.0 / (@k + vec.rank), 0) AS score
        FROM agentic.knowledge_chunks c
        JOIN agentic.knowledge_documents d ON d.id = c.document_id
        LEFT JOIN kw ON kw.id = c.id
        LEFT JOIN vec ON vec.id = c.id
        WHERE c.tenant_id = @tenant AND (kw.id IS NOT NULL OR vec.id IS NOT NULL)
        ORDER BY score DESC, c.id
        LIMIT @limit
        """;

    private const string ExactVector =
        """
            SELECT c.id, row_number() OVER (ORDER BY c.embedding <=> CAST(@vector AS vector), c.id) AS rank
            FROM agentic.knowledge_chunks c
            JOIN agentic.knowledge_documents d ON d.id = c.document_id AND d.indexed_revision = c.revision
            WHERE @vector IS NOT NULL AND c.tenant_id = @tenant AND d.tenant_id = @tenant
              AND d.source_key = ANY(@sources) AND d.embedding_model = @model AND c.embedding IS NOT NULL
            ORDER BY rank
            LIMIT @candidates
        """;

    /// <summary>
    /// The innermost query is the shape the HNSW index answers (<c>ORDER BY cast &lt;=&gt; query LIMIT n</c> under the
    /// index predicate); ranks are numbered afterwards. Only integers from validated configuration are put in the text.
    /// </summary>
    private static string IndexedVector(KnowledgeVectorIndex index) => string.Create(CultureInfo.InvariantCulture,
        $"""
             SELECT nearest.id, row_number() OVER (ORDER BY nearest.distance, nearest.id) AS rank
             FROM (
                 SELECT c.id, c.embedding::{index.ColumnType} <=> CAST(@vector AS {index.ColumnType}) AS distance
                 FROM agentic.knowledge_chunks c
                 JOIN agentic.knowledge_documents d ON d.id = c.document_id AND d.indexed_revision = c.revision
                 WHERE c.tenant_id = @tenant AND d.tenant_id = @tenant AND vector_dims(c.embedding) = {index.Dimensions}
                   AND d.source_key = ANY(@sources) AND d.embedding_model = @model
                 ORDER BY c.embedding::{index.ColumnType} <=> CAST(@vector AS {index.ColumnType})
                 LIMIT @candidates
             ) nearest
         """);

    /// <summary>pgvector's text form: <c>[0.1,0.2,...]</c> (invariant culture).</summary>
    public static string ToLiteral(float[] vector)
    {
        var text = new StringBuilder(vector.Length * 10).Append('[');
        for (var i = 0; i < vector.Length; i++)
        {
            if (i > 0)
                text.Append(',');
            text.Append(vector[i].ToString("G9", CultureInfo.InvariantCulture));
        }

        return text.Append(']').ToString();
    }
}
