namespace Attachment.Domains.Attachments.Models;

/// <summary>
/// Lifecycle of an <see cref="AttachmentFile"/> row. Only <see cref="Ready"/> rows are visible to reads (global
/// query filter in <c>AttachmentDbContext</c>); everything else is maintenance state owned by the upload handler,
/// the blob-deletion job and the reconciliation sweeper. Stored as a string; values are explicit and never 0 so
/// the CLR default is never a valid status.
/// </summary>
public enum AttachmentStatus
{
    /// <summary>Row inserted, blob not (fully) written yet. Abandoned rows are removed by the sweeper.</summary>
    Uploading = 1,

    /// <summary>Blob written, size and SHA-256 recorded. The only status reads return.</summary>
    Ready = 2,

    /// <summary>Deleted by the business (row hidden, <c>IsDeleted</c>); the blobs are removed in the background.</summary>
    PendingDeletion = 3
}
