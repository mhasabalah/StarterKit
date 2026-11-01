using Attachment.Contracts.Attachments.Dtos;

namespace Attachment.Infrastructure.Validation;

public interface IFileValidationService
{
    Task<FileValidationResult> ValidateFileAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize,
        AttachmentType attachmentType,
        CancellationToken cancellationToken = default);

    bool IsAllowedContentType(string contentType, AttachmentType attachmentType);
    bool IsWithinSizeLimit(long fileSize, AttachmentType attachmentType);
}

public record FileValidationResult(
    bool IsValid,
    List<string> Errors);
