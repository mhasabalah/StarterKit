# 0016. Run bulk imports and exports as leased, checkpointed Hangfire jobs on their own queue

## Status

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

Tenants need to load and extract thousands of rows (G19). Doing that inside an HTTP request breaks on real files:
request timeouts, memory for a whole workbook, and lost work when a pod restarts. The template already has a
durable job engine (Hangfire on Postgres) that carries the tenant into jobs (`TenantJobFilter`) and defers migrating
tenants, and a file store with content validation and malware scanning (Attachment). Business data stays in the
owning modules, which other modules may only reach through `.Contracts`.

## Decision

- **A `DataTransfer` module is the engine; owning modules plug in definitions** (`IImportDefinition` /
  `IExportDefinition` in `DataTransfer.Contracts`, registered with `AddImportDefinition` / `AddExportDefinition`).
  The engine never references a business module; definitions write through their own repositories and aggregates.
- **Hangfire, not a new worker system.** Jobs run on a dedicated queue `data-transfer` with its own small server.
  `Hangfire:Servers:<Name>:Enabled` lets the SAME image run as API-only or worker-only replicas
  (`docker-compose.worker.yml`). There is no new project, broker or pipeline.
- **The job row is the source of truth.** A lease (`LeaseOwner` / `LeaseUntil`, optimistic concurrency) prevents two
  workers from processing one job. A per-batch checkpoint (`LastCommittedRow`) makes retries resume. Hangfire's
  retry is off; our own retry schedules attempts with backoff, and a stall sweep re-queues lost jobs.
- **The requester, not the job server.** Hangfire restores only the tenant. Every attempt therefore re-reads the
  requester's membership: they must still hold the permissions, and the definition runs in their branch scope and
  language. Otherwise a branch-restricted member's export would see every branch, and a removed member's queued
  import would still write.
- **Progress is pushed, state is polled.** Each checkpoint pushes a throttled `datatransfer.job-progress` user event
  over the existing notification hub (a generic `IUserEventPublisher` contract). The job row stays the source of
  truth for reconnecting clients.
- **Plans meter rows, not jobs.** `bulk_import` / `bulk_export` gate the routes; `data_transfer_rows` is counted per
  batch in the job, against the tenant (or the `ActingUser` requester in single-tenant mode).
- **Per-batch commits.** Each batch is one unit of work of the definition (events and outbox included). Atomicity
  per file is offered as `ValidateFirst` (validate everything, write only if all rows are valid), not as one
  long transaction. Definitions must be idempotent (upsert by natural key).
- **Streaming files.** Sylvan.Data.Csv / Sylvan.Data.Excel (MIT) read CSV/XLSX row by row. Own minimal streaming
  writers produce CSV (UTF-8 BOM, formula-injection guard) and XLSX (inline strings). Files go through Attachment
  (`UploadAttachmentCommand`, new trusted `OpenAttachmentContentQuery`, signed URLs).

## Consequences

- Big files never hold an HTTP request, a long transaction or a whole workbook in memory. Imports are crash-safe
  and resumable, and a heavy workload can move to its own replicas by configuration only.
- Definitions carry a contract (idempotent upsert, no throw for bad rows, no save in a dry run) that only tests
  can check; the Catalog definition is the reference.
- A crash between a definition's save and the checkpoint replays one batch: the data is unchanged, but counters are
  at-least-once.
- Enforced by `ModuleBoundaryTests` (Contracts only), `ThirdPartyIsolationTests` (Sylvan only in DataTransfer),
  `TenantIsolationArchitectureTests` / `TenantRowLevelSecurityCoverageTests` (tenant data + RLS),
  `MessagingArchitectureTests` (event, outbox) and `BackgroundJobEventDispatchTests` (`IUnitOfWorkRunner`).

## Alternatives considered

- **Synchronous endpoints:** fail on large files and give no progress or resume.
- **A separate `Worker` host project:** duplicates the host wiring, Dockerfile and CI for no gain over a role
  switch of the same image.
- **MassTransit consumers / a broker queue:** messages suit reactions, not long jobs with progress, cancellation and
  a dashboard. The InMemory transport is not durable, and consumer timeouts and redelivery fit minute-long work badly.
- **One transaction per file:** locks and memory grow with the file, and a failure at row 199,999 loses everything.
- **CsvHelper / ClosedXML / EPPlus / MiniExcel:** CsvHelper is CSV-only, ClosedXML builds a DOM (memory),
  EPPlus is commercial. Sylvan streams both formats behind one `DbDataReader` API with an MIT license.

## Related

[DataTransfer](../Modules/DataTransfer.md).
