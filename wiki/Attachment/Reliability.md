# Attachment Module - Lifecycle, Deletion and Reconciliation

How the Attachment module keeps the database and blob storage consistent. Rule of thumb: **the database always
knows about a blob before the blob exists, and forgets it only after the blob is gone.** Whatever a crash leaves
behind is found and finished by the reconciliation sweeper.

## 1. Attachment status

`attachment.attachments.status` (string):

| Status | Meaning | Visible to reads? |
|--------|---------|-------------------|
| `Uploading` | Row inserted, blob being written (or the upload died). | no |
| `Ready` | Blob stored; real size and SHA-256 (`content_hash`) recorded. | **yes** |
| `PendingDeletion` | Deleted by the business (`is_deleted = true`); blobs being removed in the background. | no |

Every read (`GetAttachmentByIdQuery`, download, `GetAttachmentReadUrlQuery`, `GetAttachmentsByEntityQuery`,
delete-one lookup, the thumbnail job) only sees **Ready, not-deleted** rows. This is the global query filter of
`AttachmentDbContext` (set after the generic soft-delete filter, which it replaces). Maintenance code - the upload
handler's phase 2, `DeleteAttachmentsByEntity`, `AttachmentFileReaper`, `AttachmentReconciler` - uses
`IgnoreQueryFilters()` explicitly through `IAttachmentRepository.GetLiveByEntityAsync` / `GetForMaintenanceAsync`.

## 2. Upload (two-phase)

`UploadAttachmentHandler`:

1. Validate the file (type from content, size, malware), compute SHA-256, take the known length.
2. **Phase 1** - `AttachmentFile.StartUpload(...)` inserts the row as `Uploading` and commits. No event yet.
3. **Phase 2** - write the blob, then `MarkReady(size, hash, width, height)` and commit. `MarkReady` raises
   `AttachmentUploadedEvent` (avatar replacement, Identity sync) - so handlers only ever see stored files.
4. Thumbnail (images, `GenerateThumbnail = true`, `Image:EnableThumbnailGeneration`): enqueued as
   `GenerateThumbnailJob.ExecuteForAttachmentAsync` **after** the Ready commit. The upload response has
   `thumbnailUrl = null` and `thumbnailPending = true`; read the attachment later for its `thumbnailUrl`.

If phase 2 fails, the handler deletes the blob and then the row (best effort). Anything it cannot clean stays
`Uploading` and the sweeper removes it after `Reconciliation:UploadingTimeoutMinutes`.

`MarkReady` is only valid from `Uploading`, and every save is version-checked: if the sweeper or a
delete-by-entity claimed the row meanwhile, the Ready save fails with a concurrency conflict and the upload cleans
up after itself - a claimed row can never come back to life.

Pending uploads (`temp/{session}/...`) still write the blob first and the row second (inside the session lock);
the hash is stored on the pending row and carried to the attachment by associate, which creates the attachment
directly as `Ready` (`AttachmentFile.Create`) because its blobs already exist.

## 3. Delete (asynchronous)

| Command | Effect in the request | Background |
|---------|----------------------|------------|
| `DeleteAttachmentCommand` | `RequestDeletion` -> `PendingDeletion` + `is_deleted`, saved | `DeleteAttachmentFilesJob([id])` |
| `DeleteAttachmentsByEntityCommand` | every live row of the entity (**Ready and Uploading**) marked in one save; `DeletedCount` = rows marked | one `DeleteAttachmentFilesJob(ids)` |
| New avatar (`AttachmentUploadedEventHandler`) | previous avatars marked, saved | `DeleteAttachmentFilesJob(ids)` |

The request never waits for storage and never fails because of it. `DeleteAttachmentFilesJob` runs one unit of
work per id (`IUnitOfWorkRunner`) through `AttachmentFileReaper`:

- row missing -> nothing to do (**idempotent**; re-runs and retries are safe);
- row not `PendingDeletion` -> left alone;
- delete file + thumbnail (missing blobs count as deleted) -> hard-delete the row;
- a blob could not be deleted -> `deletion_attempts++`, `next_deletion_attempt_at = now + backoff`
  (`DeletionRetryBaseDelayMinutes * 2^(attempts-1)`, capped at `DeletionRetryMaxDelayMinutes`); the job then
  throws so Hangfire retries it (3x). After that the sweeper keeps retrying when the attempt is due. From
  `DeletionAttemptsAlertThreshold` failed attempts on, every failure is logged as an **error**.

If enqueueing the job fails (Hangfire storage down), the failure is logged and the sweeper picks the row up
(`next_deletion_attempt_at` is set to the request time).

Catalog's purge flow (`ProductPurgedEvent` -> `DeleteAttachmentsByEntityCommand`) works unchanged through the
contracts. Identity's avatar sync is an integration event: `AttachmentFile.MarkReady` writes
`UserAvatarChangedIntegrationEvent` to the Attachment outbox in the same transaction, and Identity's
`UserAvatarChangedConsumer` re-reads the current avatar with `GetUserAvatarQuery` (see `wiki/Architecture/Messaging.md`).

## 4. Reconciliation sweeper

`AttachmentReconciliationJob` (recurring, `Reconciliation:Cron`, UTC, no automatic retry, no concurrent runs)
runs `AttachmentReconciler`. Every DB change is its own unit of work (`IUnitOfWorkRunner`); a concurrency conflict
on one row is logged and skipped; a failing phase is logged and the next phase still runs.

1. **Abandoned uploads** - `Uploading` rows older than `UploadingTimeoutMinutes` are claimed (`RequestDeletion`,
   version-checked) and reaped (blob + row).
2. **Due deletions** - `PendingDeletion` rows whose `next_deletion_attempt_at` has passed are reaped again.
3. **Storage scan** (optional, off by default) - lists the provider (`IStorageListing`: Local, S3, Azure) and,
   for objects older than `StorageScan:SafetyWindowHours`, deletes those that **no** attachment or pending row (in
   any status) references. `StorageScan:DryRun = true` (default) only logs `orphan blob {path} would be deleted`.
   At most `StorageScan:MaxObjectsPerRun` objects are inspected per run. The safety window protects pending uploads
   and associate moves, which write the blob shortly before their row (Local storage resets the modification time
   of moved/copied files for this reason).
4. **Purge** - pending-upload rows that are soft-deleted or associated and older than `PurgeRetentionDays` are
   hard-deleted (their blobs are already gone or moved).

Turning the scan on safely: enable it with `DryRun = true`, check the log for a few runs, then set
`DryRun = false`.

## 5. Performance notes

- Downloads stream the provider stream (a `FileStream` for Local, the response stream for S3); nothing is copied
  into memory. S3/Azure downloads are redirects to signed URLs anyway.
- Read paths load the row once, untracked; the access check reuses it. Entity-policy decisions are memoized per
  request (`AttachmentAccessService`), so a policy that loads the owning entity runs once per request.
- `GetAttachmentsByEntityQuery(EntityType, EntityId, PageNumber?, PageSize?)` filters, counts and pages in SQL
  (newest first, id as tie-breaker). Without paging parameters the page size is `Paging:MaxPageSize` (100). The
  HTTP endpoint keeps returning a plain list and adds `X-Total-Count`, `X-Page-Number`, `X-Page-Size` headers.
- Uploads report the known stream length instead of re-reading object metadata (S3 HEAD / Azure properties).
- The Azure container is created once per process at startup (`AzureBlobContainerInitializer`; a failure there
  is retried by the first upload), not per upload.
- Thumbnails of direct uploads are generated by `GenerateThumbnailJob`, never inside the request.

## 6. Configuration (`Attachment:Reconciliation`)

| Key | Default | Notes |
|-----|---------|-------|
| `Enabled` | `true` | `false` removes the recurring job (abandoned uploads / failed deletions are then not retried) |
| `Cron` | `*/15 * * * *` | Hangfire cron, UTC |
| `BatchSize` | 100 | rows (or storage objects) per batch |
| `MaxBatchesPerRun` | 100 | safety stop per phase; the next run continues |
| `UploadingTimeoutMinutes` | 60 | min 5; an `Uploading` row older than this is abandoned |
| `DeletionRetryBaseDelayMinutes` | 5 | first retry delay; doubles per attempt |
| `DeletionRetryMaxDelayMinutes` | 1440 | backoff cap |
| `DeletionAttemptsAlertThreshold` | 10 | failures from this attempt on are logged as errors |
| `PurgeRetentionDays` | 30 | soft-deleted / associated pending rows older than this are purged |
| `StorageScan:Enabled` | `false` | orphan scan of the storage provider |
| `StorageScan:DryRun` | `true` | log only |
| `StorageScan:SafetyWindowHours` | 24 | min 1; newer objects are never touched |
| `StorageScan:MaxObjectsPerRun` | 10000 | objects inspected per run |

## 7. Schema (migration `AttachmentReliability`)

`attachment.attachments`:

| Column | Type | Notes |
|--------|------|-------|
| `status` | `character varying(32)` NOT NULL DEFAULT `'Ready'` | existing rows become Ready |
| `content_hash` | `character(64)` NULL | SHA-256, lowercase hex |
| `deletion_requested_at` | `timestamp with time zone` NULL | |
| `deletion_attempts` | `integer` NOT NULL DEFAULT 0 | |
| `next_deletion_attempt_at` | `timestamp with time zone` NULL | |

Indexes: `(content_hash)`, `(status, created_at)`, `(status, next_deletion_attempt_at)`.

`attachment.pending_attachments`: `content_hash character(64) NULL`.

```bash
dotnet ef migrations add AttachmentReliability --context AttachmentDbContext \
  --project src/Modules/Attachment/Attachment --startup-project src/Api --output-dir Data/Migrations
```

## 8. Tests

`src/Modules/Attachment/Attachment.Tests`:

- `Integration/AttachmentLifecycleTests` (real DI composition over Testcontainers PostgreSQL, `TestSupport/AttachmentHostFixture`):
  Uploading -> Ready with size/hash, non-Ready rows invisible to every read path, blob-write failure (with and
  without failed cleanup) and the sweeper, sweeper/upload race, delete hides at once + job deletes blobs and is
  idempotent, failed blob delete backoff and sweeper retry, delete-by-entity incl. in-flight uploads, avatar
  replacement, thumbnail job (events published, non-Ready rows skipped, pending thumbnails), streaming download,
  SQL paging.
- `Integration/ReconciliationTests`: storage scan (safety window, dry run, row and pending-row references, off by
  default), purge retention, the recurring job end to end.
- `Unit/AttachmentLifecycleUnitTests`: entity state machine, hash format, backoff, options validation, delete
  handlers, reaper guards, per-request policy memoization.
- `Unit/ConsistencyTests`: two-phase upload compensation; `Unit/StorageProviderTests`: Local listing / move
  mtime / file stream / known length, Azure container gate.
