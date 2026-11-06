# Attachment Module

File upload, storage (Local / Azure Blob / S3) and access control for StarterKit.

**Start with [Security.md](Security.md)**: it is the reference for entity access policies, server-issued
pending sessions, upload validation, signed read URLs, the download endpoint and storage consistency.

## Quick Start

### 1. Configuration (appsettings.json)
```json
{
  "Attachment": {
    "Storage": { "Provider": "Local", "Local": { "UploadPath": "uploads" } },
    "Validation": { "MaxImageSizeBytes": 10485760, "MaxImageMegapixels": 40 },
    "Download": { "ReadUrlTtlMinutes": 5 },
    "Image": { "EnableThumbnailGeneration": true }
  }
}
```

### 2. Upload a file
```bash
curl -X POST "/api/v1/attachments/upload" \
  -H "Authorization: Bearer {token}" \
  -F "file=@avatar.jpg" \
  -F "entityType=User" \
  -F "entityId={your-user-id}"
```

The server detects the real type from the content; the stored extension and content type are its decision.
Attaching to an entity requires that entity type's access policy to allow you (built-in: `User` = yourself).

### 3. Response
```json
{
  "isSuccess": true,
  "data": {
    "id": "guid",
    "fileName": "{id}.jpg",
    "fileUrl": "/api/v1/attachments/{id}/download",
    "thumbnailUrl": null,
    "fileSize": 20480,
    "contentType": "image/jpeg"
  }
}
```

Thumbnails are generated in the background after the upload; read the attachment
(`GET /api/v1/attachments/{id}`) for its `thumbnailUrl` once it is there. The attachment DTO also carries
`contentHash` (SHA-256 of the file).

`fileUrl` is stable: every request is access-checked and then redirected to a short-lived signed URL
(S3/Azure) or streamed (Local). Do not store provider URLs.

## API Endpoints

| Method | Route | Auth | Notes |
|--------|-------|------|-------|
| POST | `/api/v1/attachments/upload` | user | entity policy enforced; body limit = max configured size |
| GET | `/api/v1/attachments/{id}` | user | metadata (public / owner / admin / entity policy) |
| GET | `/api/v1/attachments/{id}/download[?thumbnail=true]` | anonymous for public files | 302 to signed URL or stream |
| DELETE | `/api/v1/attachments/{id}` | user | admin, entity policy, or uploader of an unlinked file; hidden at once, files removed in the background |
| GET | `/api/v1/attachments/entity/{entityType}/{entityId}[?pageNumber=&pageSize=]` | user | all files if policy allows read, else public + own; newest first; `X-Total-Count` header |
| POST | `/api/v1/attachments/pending/sessions` | anonymous | issues an owner-bound session id |
| POST | `/api/v1/attachments/pending/upload` | anonymous | omit `sessionId` to get a new session |
| GET | `/api/v1/attachments/pending/session/{sessionId}` | session owner | signed preview URLs |
| DELETE | `/api/v1/attachments/pending/{id}?sessionId=` | session owner | |
| POST | `/api/v1/attachments/pending/associate` | user | session owner + entity policy |

## For other modules

- Register an access policy for your entity type: `services.AddAttachmentEntityPolicy<TPolicy>(EntityType.X)`.
- Upload / associate / delete via `ISender` with the commands in `Attachment.Contracts` (trusted calls: you
  authorize the entity yourself).
- Need to hand a private file to a client after your own checks? Send `GetAttachmentReadUrlQuery` and
  redirect to the returned URL (see Subscriptions `GetPaymentProof` / `GetInvoicePdf`).
- Need the bytes in the background (e.g. a bulk import job)? Send `OpenAttachmentContentQuery` (trusted, no check)
  and dispose the returned stream. DataTransfer stores its files as `EntityType.DataTransferJob`.

## Related Documentation

- [Security.md](Security.md) - access model, validation, downloads, consistency, config keys, tests
- [Reliability.md](Reliability.md) - upload/delete lifecycle, background deletion, reconciliation sweeper, schema
- [Configuration.md](Configuration.md) - option reference (see Security.md section 8 for new keys)
- [Uploads.md](Uploads.md) - direct uploads and the pending (session) upload flow for frontends (Security.md wins on conflicts)
- [Storage-Providers.md](Storage-Providers.md) - Local / Azure Blob / S3: choosing, configuring, operating
- [Image-Processing.md](Image-Processing.md) - thumbnails and image validation behind `IImageProcessingService` (SkiaSharp); switching libraries
- [appsettings.Attachment.example.json](appsettings.Attachment.example.json) - complete example configuration section
