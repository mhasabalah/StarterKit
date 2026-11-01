using Shared.Contracts.CQRS;

namespace Attachment.Contracts.Attachments.Commands;

public record DeleteAttachmentCommand(Guid Id) : ICommand<DeleteAttachmentResult>;

public record DeleteAttachmentResult(bool Success);
