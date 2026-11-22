# 0032. Index knowledge vectors with HNSW per configured dimension, built after migrations

Large knowledge corpora can be searched through an approximate HNSW index. The column stays untyped, and the index is
a partial expression index for the configured embedding dimension. The deploy-time migrator builds it through a new
post-migration hook.

## Status

- **Status:** Accepted
- **Date:** 2026-11-22

## Context

[ADR-0028](0028-agent-knowledge-and-memory-on-pgvector.md) stores passage vectors in an untyped `vector` column, so the
dimension belongs to the embedding model and not to the schema. It searches them exactly: every vector of the
tenant's sources is compared with the query. That is precise and fine up to tens of thousands of passages, but its
cost grows with the corpus.

pgvector's HNSW index needs a typed column of one dimension (`vector` up to 2000, `halfvec` up to 4000). The dimension
is configuration (`Agentic:Knowledge:Dimensions`), and a migration cannot read configuration. The rules say migrations
run at deploy time, never at boot in production, and that nothing else does migrate work at startup.

## Decision

- **A partial expression index per dimension.**
  `CREATE INDEX CONCURRENTLY ix_knowledge_chunks_embedding_hnsw_<n> ON agentic.knowledge_chunks USING hnsw ((embedding::vector(<n>)) vector_cosine_ops) WITH (m, ef_construction) WHERE vector_dims(embedding) = <n>`.
  Above 2000 dimensions it uses `halfvec(<n>)`. Rows of other dimensions are simply not in the index.
- **Opt in.** `Agentic:Knowledge:VectorIndex=Hnsw` (default `None`, exact search) requires `Dimensions`. `HnswM` (16),
  `HnswEfConstruction` (64) and `HnswEfSearch` (100, at least `SearchCandidates`) are validated at startup.
- **Built by a post-migration step.** `Shared.Core.Data.IPostMigrationStep` runs after a context's migrations:
  - Where it runs: the shared database and every dedicated tenant database, under the same advisory lock and on the
    migration connection.
  - When it runs: `--migrate-only` at deploy time, and boot-time migration in development and tests.
  - `KnowledgeVectorIndexStep` builds the index CONCURRENTLY, so writes go on. It drops and rebuilds an invalid index
    left by an interrupted build, and does nothing when the index is already valid.
- **Search uses it only when it can.** With the index on and a query vector of the configured length, the vector side
  of the hybrid search becomes the shape HNSW answers. The innermost query is
  `ORDER BY embedding::vector(<n>) <=> query LIMIT candidates` under the index predicate, and ranks are numbered after.
  - Settings: `SET LOCAL hnsw.iterative_scan = relaxed_order` and `hnsw.ef_search`, in a transaction, so the tenant
    and source filters still leave enough candidates and nothing leaks to the pooled connection.
  - The tenant is still named in the SQL, and RLS still applies.
  - Only validated integers are put into the statement text.

## Consequences

- Large corpora search in roughly logarithmic time. Results are approximate. On the test corpus they match exact
  search, and keyword search in the same query still catches exact words.
- Changing the embedding dimension needs a new index (the step builds the one for the new dimension; drop the old one
  by hand) and a re-index of the documents, as before.
- Building the index on a large table takes time and memory at deploy (`maintenance_work_mem`). The migrator's command
  timeout is two hours.
- `IPostMigrationStep` is the one place for configuration-dependent schema work. Seeders stay for data.
- Enforced by:
  - `KnowledgeVectorIndexTests` (integration): built and idempotent, rebuilt when invalid, absent when off, used by
    the query plan, same top results as exact search, tenant isolation.
  - `KnowledgeVectorIndexTests` (unit): type per dimension, validation.
  - `KnowledgeEvaluationTests`: a real embedding model ranks paraphrased English and Arabic questions to the right
    passage; skipped without a provider.

## Alternatives considered

- **Type the column (`vector(1536)`) in a migration.** Bakes one model's dimension into the schema of every product
  and breaks ADR-0028's model independence. Rejected.
- **IVFFlat.** Needs training data at build time and re-building as the corpus grows; HNSW does not. Rejected.
- **Build the index from the application at startup.** Production must never do schema work at boot. Rejected.
- **A seeder.** Seeders run on the runtime connection as a tenant and are for data, not DDL. Rejected.

## Related

[Agentic module](../Agentic/Knowledge-And-Memory.md), [ADR-0028](0028-agent-knowledge-and-memory-on-pgvector.md),
[Agentic operations](../deployment/Agentic-Operations.md), [Database conventions](../Architecture/DatabaseConventions.md),
[ADR index](README.md).
