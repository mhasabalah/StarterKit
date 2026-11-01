using Attachment.Contracts.Attachments.Dtos;
using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Queries;

public record GetAttachmentsByEntityQuery(EntityType EntityType, Guid EntityId)
    : IQuery<GetAttachmentsByEntityResult>;

public record GetAttachmentsByEntityResult(List<AttachmentDto> Attachments);
