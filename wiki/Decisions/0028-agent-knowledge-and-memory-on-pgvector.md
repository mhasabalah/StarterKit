# 0028. Give agents knowledge and memory on PostgreSQL with pgvector, behind the same permissions

Agents search only what a module chose to index and what the user a run acts for may read, in the database the
tenant's data already lives in.

## Status

- **Status:** Accepted
- **Date:** 2026-11-14

## Context

Agents need to answer from a tenant's documents (retrieval-augmented generation) and to keep a few facts between runs.
Both are new ways for data to reach a model, and both are new ways to attack one: a document can carry instructions,
and a memory that steers every later run is an attractive target. The platform already decides tool calls in code
([ADR-0025](0025-agentic-module-and-model-seam.md)) and asks a person before risky changes
([ADR-0027](0027-agent-approvals-triggers-and-policies.md)). Open questions were where vectors live, how access is
decided, how indexing survives failures, and how content is erased.

## Decision

**Storage: pgvector in the module's own tables.**

- PostgreSQL with the `vector` extension: the repository's image (`infra/postgres`) builds pgvector from source,
  Aspire uses the pgvector image of its PostgreSQL major, and test containers use `pgvector/pgvector`. The Agentic
  migration runs `CREATE EXTENSION IF NOT EXISTS vector` (a no-op when an administrator created it already).
- `knowledge_documents` (one per source and owner id, with a revision) and `knowledge_chunks` (passages) are tenant
  data with forced RLS, like every Agentic table. Two chunk columns are managed in SQL, not by EF: `embedding vector`
  (untyped, so changing the model's dimension needs no migration) and `search_vector tsvector` (generated,
  `'simple'` configuration so Arabic and English behave alike). The EF model needs no provider plugin for them.
- Search is exact (no ANN index): per tenant, a sequential distance computation is fast enough for the document
  counts of a business tenant and returns exact results. An HNSW index per embedding model is the next step when a
  tenant outgrows it.

**What is searchable: only what a module indexes.**

- A module registers a source in code (`AddKnowledgeSource`: key and `RequiredPermission`, or explicitly
  `AllowAnyMember`) and sends `IndexKnowledgeDocumentCommand` with text or an attachment id from its own consumer or
  handler. There is no generic upload endpoint and no crawling.
- Indexing runs on the `agentic` queue: extract (plain text, Markdown, CSV, JSON, HTML without markup, PDF text layer
  through PdfPig), split into overlapping passages, embed, then swap the old and new passages in one transaction. A
  job accepts its result only while its revision is still current, and attempts that never returned (a worker that
  died on a document) count, so a poisonous document fails instead of being retried forever. Content that can never be indexed fails at once
  with a code; a provider failure is retried with backoff and then fails. A stall sweep re-queues lost jobs.

**Who sees what: decided in code, before and after SQL.**

- Retrieval is the built-in read tool `knowledge.search`, available to agents that list it. Its results are untrusted
  content, so the run is tainted and every later change needs a person (ADR-0027).
- The searchable sources are those whose permission the run holds NOW: the initiator's current permissions narrowed
  by an API-key ceiling. Sources the model names can only narrow that set. The query is internal to the module and
  answers only inside an executing tool call.
- The SQL names the tenant and runs under RLS. Then the owning module's optional `IKnowledgeSourcePolicy` filters the
  hits per document, as the initiator.
- Keyword and vector results are merged by reciprocal rank fusion. Every passage carries a citation
  (`source/document#passage`). Without a query embedding the search falls back to keywords.

**Memory: written by people, or by agents with consent.**

- `agent_memories` holds personal memories (one user) and tenant memories (everyone; only `agentic.manage` writes
  them). People write them through `/api/v1/agent-memories`; the write commands are internal to the module (not in
  Contracts), so no other module's code an agent drives can write one.
- An agent can only PROPOSE a personal memory with `memory.remember`, a tool marked `RequiresApproval`: every call
  waits for a person to approve the exact text, whatever the autonomy level. Recall is the read tool `memory.recall`,
  so memory is data in the transcript and never part of the instructions. Memories expire
  (`Agentic:Memory:RetentionDays`) and are bounded per user and tenant.

**Lifecycle.**

- `AttachmentDeletedIntegrationEvent` (new, Attachment outbox) removes documents indexed from a deleted file.
- `UserDeletedIntegrationEvent`: personal memories are deleted, the user's open runs are cancelled, and the content of
  their finished runs is erased in every tenant (facts, counters and audit entries stay).
- `Agentic:Content:RetentionDays` erases the content of old finished runs (`PurgeAgentContentJob`).

## Consequences

- Every product of the template needs pgvector. The repository's images provide it; managed PostgreSQL services
  offer it as an allow-listed extension. A migration role that may not create extensions needs an administrator to
  run `CREATE EXTENSION vector` once per database (the role script does it).
- Knowledge never widens access: a passage reaches a model only if the user could read the source, the tenant
  matches, and the owning module's policy agrees. An instruction hidden in a document can at most ask for a change,
  which a person then sees.
- Changing the embedding model leaves old passages without comparable vectors until the owning module sends the
  documents again (the same content is then indexed again, because its model differs); keyword search keeps working
  meanwhile.
- A changed document keeps serving its last indexed passages (`indexed_revision`) until the new ones replace them.
- Exact search costs grow with a tenant's passage count; this is the known scaling limit.
- Pinned by `Agentic.IntegrationTests` (`KnowledgeTests`, `AgentMemoryTests`) and `Agentic.Tests/KnowledgeTests`.

## Alternatives considered

- **A separate vector database:** a second store to secure, back up, isolate per tenant and keep consistent with
  PostgreSQL, for volumes PostgreSQL handles.
- **Mapping the vector column through an EF provider plugin:** couples the module to a plugin that must track
  Npgsql's internal extension points release by release; two SQL statements do the job.
- **Indexing everything a tenant uploads:** makes every file reachable by agents without the owning module deciding
  who may read it.
- **Injecting memories into the system prompt:** makes stored text part of the instructions; a planted memory would
  steer every run.
- **Letting agents write memories freely:** a prompt injection could persist itself.

## Related

[Agentic module](../Modules/Agentic.md), [ADR-0025](0025-agentic-module-and-model-seam.md),
[ADR-0027](0027-agent-approvals-triggers-and-policies.md), [Attachment](../Attachment/README.md).
