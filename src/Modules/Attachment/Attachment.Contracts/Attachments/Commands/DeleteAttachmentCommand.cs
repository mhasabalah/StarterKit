using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Commands;

/// <summary>
/// Deletes one attachment (file + thumbnail + row). The attachment disappears from every read immediately
/// (soft delete, saved by this command); its files and row are removed by a background job, retried until it
/// succeeds.
/// <para>
/// Authorization: Admins may always delete. Otherwise, when <see cref="EnforceEntityAccessPolicy"/> is
/// <c>false</c> (default, used by trusted module code that already authorized the operation) the current
/// user must be the uploader. When it is <c>true</c> (the public HTTP endpoint) and the attachment is linked
/// to an entity, the entity type's <c>IAttachmentEntityAccessPolicy.CanAttachAsync</c> must allow it
/// (deny by default); an attachment without an entity can be deleted by its uploader.
/// </para>
/// </summary>
public record DeleteAttachmentCommand(Guid Id) : ICommand<DeleteAttachmentResult>
{
    /// <summary>Set by the Attachment HTTP endpoint. See the type summary.</summary>
    public bool EnforceEntityAccessPolicy { get; init; }
}

public record DeleteAttachmentResult(bool Success);
