using Attachment.Contracts.Attachments.Dtos;
using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Commands;

/// <summary>
/// Command to upload an attachment file.
/// Category is automatically calculated from EntityType + Type - do not specify it.
/// <para>
/// The attachment is visible to reads only once the file is fully stored (two-phase upload); the result is
/// returned after that point.
/// </para>
/// <para>
/// The stored extension and content type are determined by the server from the file content (magic bytes);
/// <see cref="ContentType"/> and the extension of <see cref="FileName"/> must agree with that content.
/// </para>
/// <para>
/// When <see cref="EnforceEntityAccessPolicy"/> is <c>true</c> (set by the public HTTP endpoint) and an entity
/// is given, the entity type's <c>IAttachmentEntityAccessPolicy.CanAttachAsync</c> must allow the current user
/// (deny by default). Module code that already authorized the entity leaves it <c>false</c>.
/// </para>
/// </summary>
public record UploadAttachmentCommand(
    string FileName,
    string ContentType,
    long FileSize,
    Stream FileStream,
    AttachmentType Type,
    EntityType? EntityType = null,
    Guid? EntityId = null,
    string? Description = null,
    bool IsPublic = false,
    bool GenerateThumbnail = true,
    Guid? UploadedBy = null)
    : ICommand<UploadAttachmentResult>
{
    /// <summary>Set by the Attachment HTTP endpoint. See the type summary.</summary>
    public bool EnforceEntityAccessPolicy { get; init; }
}

/// <param name="FileUrl">Stable API URL (<c>/api/v1/attachments/{id}/download</c>); access is checked per request.</param>
/// <param name="ThumbnailUrl">
/// Stable API URL of the thumbnail, or null. Thumbnails are generated in the background after the upload, so this
/// is null at upload time; read the attachment (<c>ThumbnailUrl</c> of <see cref="Dtos.AttachmentDto"/>) later.
/// </param>
/// <param name="ThumbnailPending">True when a thumbnail is being generated in the background.</param>
public record UploadAttachmentResult(
    Guid Id,
    string FileName,
    string FileUrl,
    string? ThumbnailUrl,
    long FileSize,
    string ContentType,
    bool ThumbnailPending = false);
