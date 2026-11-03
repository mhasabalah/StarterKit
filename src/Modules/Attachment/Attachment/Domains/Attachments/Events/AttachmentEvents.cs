using Attachment.Domains.Attachments.Models;
using Shared.Contracts.Events;

namespace Attachment.Domains.Attachments.Events;

public record AttachmentUploadedEvent(AttachmentFile Attachment) : IDomainEvent;

public record AttachmentUpdatedEvent(AttachmentFile Attachment) : IDomainEvent;

public record AttachmentDeletedEvent(Guid Id, string StoragePath, string? ThumbnailPath) : IDomainEvent;

public record AttachmentLinkedToEntityEvent(Guid AttachmentId, EntityType EntityType, Guid EntityId) : IDomainEvent;

public record AttachmentThumbnailGeneratedEvent(Guid AttachmentId, string ThumbnailUrl) : IDomainEvent;

public record AttachmentMadePublicEvent(Guid AttachmentId) : IDomainEvent;

public record AttachmentMadePrivateEvent(Guid AttachmentId) : IDomainEvent;

// Pending Attachment Events
public record PendingAttachmentUploadedEvent(PendingAttachment PendingAttachment) : IDomainEvent;

public record PendingAttachmentDeletedEvent(
    Guid Id,
    Guid SessionId,
    string StoragePath,
    string? ThumbnailPath) : IDomainEvent;

public record PendingAttachmentAssociatedEvent(Guid Id, Guid SessionId) : IDomainEvent;

public record PendingAttachmentThumbnailGeneratedEvent(
    Guid Id,
    Guid SessionId,
    string ThumbnailUrl) : IDomainEvent;
