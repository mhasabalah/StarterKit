# Attachment Module - Security, Access and Storage Model

This page is the reference for how the Attachment module authorizes access, validates uploads, stores
files and hands them out. It supersedes older statements in the other Attachment pages where they differ.

## 1. Entity access policies (who may attach / read)

An attachment can be linked to an entity (`EntityType` + `EntityId`). The Attachment module does not know
the business rules of other modules, so **the module that owns an entity registers a policy** for it.

```csharp
// Attachment.Contracts/Authorization/IAttachmentEntityAccessPolicy.cs
public interface IAttachmentEntityAccessPolicy
{
    Task<bool> CanAttachAsync(Guid entityId, AttachmentAccessContext caller, CancellationToken ct = default);
    Task<bool> CanReadAsync(Guid entityId, AttachmentAccessContext caller, CancellationToken ct = default);
}
```

Rules:

- **Deny by default.** No policy registered for an entity type = the public endpoints refuse to attach to it
  and only list its `IsPublic` files (plus the caller's own uploads).
- **Admins** (`Admin`, `SuperAdmin`) are always allowed; policies are not consulted for them.
- Built-in: `EntityType.User` -> `UserAttachmentEntityAccessPolicy` (self or admin).

### Registering a policy from another module

Only `Attachment.Contracts` is needed (module boundary rules hold):

```csharp
// Catalog/Domains/Products/Authorization/ProductAttachmentAccessPolicy.cs
public sealed class ProductAttachmentAccessPolicy(ISender sender) : IAttachmentEntityAccessPolicy
{
    public async Task<bool> CanAttachAsync(Guid productId, AttachmentAccessContext caller, CancellationToken ct = default)
        => caller.UserId is { } userId && await IsOwnerAsync(productId, userId, ct);

    public Task<bool> CanReadAsync(Guid productId, AttachmentAccessContext caller, CancellationToken ct = default)
        => CanAttachAsync(productId, caller, ct);
    // ...
}

// CatalogModule.AddCatalogModule(...)
services.AddAttachmentEntityPolicy<ProductAttachmentAccessPolicy>(EntityType.Product);
```

Policies are scoped keyed services (key = `EntityType`); registering again for the same type replaces it.
Until Catalog registers a Product policy, Product attachments can only be managed through Catalog's own
endpoints (trusted calls, see below) or by admins.

### Where policies are enforced

| Operation | HTTP endpoint (`EnforceEntityAccessPolicy = true`) | Trusted module call (default `false`) |
|-----------|-----------------------------------------------------|----------------------------------------|
| `UploadAttachmentCommand` with an entity | `CanAttachAsync` must allow | not checked (caller authorized the entity) |
| `AssociatePendingAttachmentsCommand` | session owner **and** `CanAttachAsync` | session owner only |
| `DeleteAttachmentCommand` | admin, or `CanAttachAsync` for linked files, or uploader for unlinked files | admin or uploader |
| `GetAttachmentsByEntityQuery` | `CanReadAsync` -> all files; otherwise public + own uploads (SQL filter) | same |
| `GetAttachmentByIdQuery`, download | public, admin, uploader, or `CanReadAsync` | same |
| `GetAttachmentReadUrlQuery` | (no endpoint) | **no check** - only for callers that already authorized |

`EnforceEntityAccessPolicy` is an `init` property on the command records; only the Attachment endpoints set it.

## 2. Pending upload sessions (server-issued, owner-bound)

Session ids are no longer chosen by the client.

- `POST /api/v1/attachments/pending/sessions` returns `{ sessionId }`, or omit `sessionId` on
  `POST /attachments/pending/upload` and use the id returned in the response.
- A session id is a normal GUID whose bytes are `nonce(8) || HMAC(owner, nonce)(8)`. The owner is the
  authenticated user, or for anonymous uploads a fingerprint of client IP + User-Agent. Nothing is stored;
  ownership is proven by recomputing the MAC.
- Upload, list, delete and reorder require `IsOwnedByCurrentCaller`; associate requires the session to be
  owned by the associating user **or** by the anonymous fingerprint of the current request (anonymous
  upload, then login, then create the entity from the same browser).
- A random or foreign GUID -> `403`. This also closes the "new GUID per file" bypass of
  `MaxUploadsPerSession`; issuing sessions is rate limited by the `upload` policy.
- Concurrent uploads to one session are serialized with a PostgreSQL advisory lock: the limit holds and a
  duplicate `displayOrder` returns `409` instead of a unique-index `500`.

## 3. Upload validation (C5)

`FileValidationService` runs for every upload (direct and pending):

1. File name reduced to a safe display name (no directory part, no control characters).
2. Extension: a built-in block list (`.html .htm .svg .js .exe .docm ...`) always wins, then
   `Validation:AllowedExtensions` and `Validation:BlockedExtensions`.
3. The extension selects a rule in `FileSignatureCatalog`; extensions without a rule are rejected.
4. **Magic bytes** must match the rule (JPEG/PNG/GIF/BMP/WebP, PDF, OLE and OOXML (zip structure checked,
   macros rejected), MP4/MOV/MPEG/AVI, MP3/WAV/OGG). Text formats (`.txt .csv .json .xml`) must be UTF-8
   without HTML/script markup; XML with `DOCTYPE`/`ENTITY` is rejected.
5. The declared content type must agree with the rule (`application/octet-stream` = not declared).
6. Size limits use the real stream length.
7. Images: `Image.IdentifyAsync` (header only) must succeed and the format must match; images above
   `Validation:MaxImageMegapixels` are rejected before any decoding.
8. Optional malware scan (`IMalwareScanner`).

**The stored extension, content type and attachment type are server-determined** from the rule; storage
keys are `yyyy/MM/dd/{id}.ext` (no client file name in storage).

Thumbnails decode with `DecoderOptions.TargetSize` and `MaxFrames = 1`; EXIF is stripped.

### Malware scanning

```csharp
services.AddSingleton<IMalwareScanner, ClamAvScanner>(); // after AddAttachmentModule
```

Set `Attachment:Validation:EnableVirusScanning=true`. If scanning is enabled and only the built-in
`NoOpMalwareScanner` is registered, the application **fails at startup**.

### Request body limits

The upload endpoints set `RequestSizeLimit` and the multipart limit to the largest configured
`Max*SizeBytes` + 64 KB, so oversized bodies are cut off by Kestrel (`413`).

## 4. Private files and downloads (C13)

- `StoragePath` is the source of truth. URLs are never handed out from the database.
- DTO `FileUrl` / `ThumbnailUrl` are **stable API URLs**: `/api/v1/attachments/{id}/download[?thumbnail=true]`
  (prefixed with `Download:PublicBaseUrl` when set). Pending DTOs get fresh signed preview URLs.
- `GET /api/v1/attachments/{id}/download` (anonymous allowed for public files) checks access, then:
  - S3: `302` to a presigned GET (`Download:ReadUrlTtlMinutes`, default 5) with `Content-Disposition`
    and `Content-Type` response overrides.
  - Azure: `302` to a read-only SAS (account key or user-delegation key).
  - Local: streams the file.
- Files served by the API carry `X-Content-Type-Options: nosniff`, `Content-Security-Policy: sandbox`,
  and `Content-Disposition: attachment` except for `Download:InlineContentTypes` (raster images).
- Local storage is **not** exposed as static files anymore. Local signed URLs point at
  `/api/v1/attachments/files?p=&e=&n=&t=&i=&s=` (HMAC over path, expiry and response headers).
- Modules that authorize access themselves use `GetAttachmentReadUrlQuery` (Subscriptions payment proofs
  and invoice PDFs redirect to it after their subscriber/admin checks).

## 5. Storage providers

- Provider settings are validated at startup; there is **no fallback** to Local.
- S3: leave `AccessKeyId` and `SecretAccessKey` empty to use the default AWS credential chain (IAM role).
  Objects are private; `UsePublicAccess`/`CloudFrontDomain` are ignored.
- Azure: the container is created private; the connection string must allow SAS signing (account key) or the
  client must be able to get a user-delegation key.
- Local: storage keys use `/`; `LocalFileStorageService.ResolveFullPath` combines them with the upload root
  via `Path.Combine`/`GetFullPath` and rejects anything outside the root (`..`, absolute paths, backslashes,
  drive letters) with `InvalidStoragePathException` (400). Works the same on Linux and Windows.

## 6. DB / blob consistency

Full lifecycle, background deletion and the reconciliation sweeper: [Reliability.md](Reliability.md).

| Flow | Rule |
|------|------|
| Upload | two-phase: row inserted as `Uploading` (invisible), blob written, row marked `Ready` (size + SHA-256). Failure: blob and row removed; leftovers are swept |
| Pending upload | blob written first (inside the session lock); if `SaveChanges` fails the blob is deleted; crash leftovers are found by the optional storage scan |
| Associate | files moved, then one `SaveChanges` (rows created `Ready`); on failure files are moved back to their pending paths |
| Delete / delete-by-entity / avatar replace | row(s) marked `PendingDeletion` (hidden) and saved in the request; `DeleteAttachmentFilesJob` deletes the blobs, then the rows; failures retried with backoff by Hangfire and the sweeper |
| Thumbnail job (pending + direct uploads) | runs through `IUnitOfWorkRunner` (events published after commit); skips non-Ready/deleted/expired/associated rows; deletes its thumbnail blob if saving fails |
| Pending cleanup | loops batches of `Pending:CleanupBatchSize` until done; runs every `Pending:CleanupIntervalHours`; old soft-deleted/associated rows are purged by the sweeper |

## 7. Errors

Handlers throw the shared exceptions only: `ForbiddenException` (403), `UnauthorizedException` (401),
`BadRequestException`/`InvalidFileException` (400), `NotFoundException` (404), `ConflictException` (409),
`FileStorageException` (500, generic message).

## 8. Configuration keys

| Key | Default | Notes |
|-----|---------|-------|
| `Attachment:Download:ReadUrlTtlMinutes` | 5 | signed URL lifetime (1-10080) |
| `Attachment:Download:PublicBaseUrl` | empty | absolute origin for DTO URLs |
| `Attachment:Download:InlineContentTypes` | jpeg/png/gif/webp/bmp | everything else is `attachment` |
| `Attachment:Download:SigningKey` | empty | >= 32 chars; else derived from `JwtSettings:SecretKey`; else random per process (warning) |
| `Attachment:Validation:MaxImageMegapixels` | 40 | decompression-bomb guard |
| `Attachment:Validation:EnableVirusScanning` | false | requires an `IMalwareScanner` |
| `Attachment:Pending:CleanupBatchSize` / `CleanupIntervalHours` | 100 / 6 | now actually used |
| `Attachment:Reconciliation:*` | see [Reliability.md](Reliability.md#6-configuration-attachmentreconciliation) | sweeper schedule, timeouts, backoff, retention, storage scan |

List options (`AllowedExtensions`, `Allowed*Types`, `BlockedExtensions`, `InlineContentTypes`) use their
defaults only when not configured; a configured list **replaces** the default (it is no longer appended).

Obsolete (still bind, ignored): `Storage:Local:BaseUrl`, `Storage:S3:UsePublicAccess`,
`Storage:S3:CloudFrontDomain`, `Storage:S3:PresignedUrlExpirationMinutes`, `Storage:AzureBlob:UsePublicAccess`,
`Validation:EnableContentVerification`, `Pending:MaxUploadsPerMinute`.

## 9. Tests

`src/Modules/Attachment/Attachment.Tests` (`dotnet test src/Modules/Attachment/Attachment.Tests`):

- `Unit/FileValidationServiceTests` - spoofing, blocked types, magic bytes, size, megapixel bomb, text markup, scanner.
- `Unit/EntityAccessPolicyTests` - self/other/admin/anonymous x User, deny-by-default, custom policy, delete rules.
- `Unit/PendingSessionServiceTests` - forged/foreign/tampered sessions, anonymous fingerprint.
- `Unit/LocalStorageContractTests` - provider contract on the current OS, path traversal, signed URLs.
- `Unit/DownloadAuthorizationTests` - public/private x owner/other/admin/anonymous, redirect vs stream.
- `Unit/ConsistencyTests` - two-phase upload compensation, cleanup keeps rows whose delete failed.
- Lifecycle / deletion / sweeper / paging / streaming tests: see [Reliability.md](Reliability.md#8-tests).
- `Integration/PendingAttachmentFlowTests` - Testcontainers PostgreSQL: session ownership, concurrent limit,
  409 on duplicate order, associate, cleanup, DB failure compensation.
