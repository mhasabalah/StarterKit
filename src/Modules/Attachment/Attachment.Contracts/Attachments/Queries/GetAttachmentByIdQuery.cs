using Attachment.Contracts.Attachments.Dtos;
using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Queries;

public record GetAttachmentByIdQuery(Guid Id) : IQuery<GetAttachmentByIdResult>;

public record GetAttachmentByIdResult(AttachmentDto Attachment);
