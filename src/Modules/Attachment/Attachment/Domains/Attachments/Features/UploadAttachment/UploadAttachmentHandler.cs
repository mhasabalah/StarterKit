using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Data.Repository.Attachments;
using Attachment.Infrastructure.ImageProcessing;
using Attachment.Infrastructure.Storage;
using Attachment.Infrastructure.Validation;
using Attachment.Resources;
using Microsoft.Extensions.Localization;

namespace Attachment.Domains.Attachments.Features.UploadAttachment;

public class UploadAttachmentCommandValidator : AbstractValidator<UploadAttachmentCommand>
{
    public UploadAttachmentCommandValidator(IStringLocalizer<AttachmentResource> localizer)
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage(_ => localizer["Validation.FileName.Required"])
            .MaximumLength(500).WithMessage(_ => localizer["Validation.FileName.MaxLength"]);

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage(_ => localizer["Validation.ContentType.Required"]);

        RuleFor(x => x.FileSize)
            .GreaterThan(0).WithMessage(_ => localizer["Validation.FileSize.Min"]);

        RuleFor(x => x.FileStream)
            .NotNull().WithMessage(_ => localizer["Validation.FileStream.Required"]);

        // UploadedBy is now optional - will be retrieved from HTTP context if not provided
    }
}

public class UploadAttachmentHandler(
    IAttachmentRepository repository,
    IStorageService storageService,
    IImageProcessingService imageProcessingService,
    IFileValidationService fileValidationService,
    IHttpContextService httpContextService,
    ILogger<UploadAttachmentHandler> logger)
    : ICommandHandler<UploadAttachmentCommand, UploadAttachmentResult>
{
    public async Task<UploadAttachmentResult> Handle(
        UploadAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        var uploadedBy = command.UploadedBy
            ?? httpContextService.GetUserIdAsGuid()
            ?? throw new UnauthorizedAccessException("User must be authenticated to upload files");

        // Validate file
        var validationResult = await fileValidationService.ValidateFileAsync(
            command.FileStream,
            command.FileName,
            command.ContentType,
            command.FileSize,
            command.Type,
            cancellationToken);

        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException(
                $"File validation failed: {string.Join(", ", validationResult.Errors)}");
        }

        // Upload file to storage
        var storageResult = await storageService.UploadAsync(
            command.FileStream,
            command.FileName,
            command.ContentType,
            cancellationToken);

        string? thumbnailPath = null;
        string? thumbnailUrl = null;
        int? width = null;
        int? height = null;

        // Generate thumbnail for images if requested
        if (command.GenerateThumbnail && command.Type == AttachmentType.Image)
        {
            try
            {
                command.FileStream.Position = 0; // Reset stream
                
                var thumbnailResult = await imageProcessingService.GenerateThumbnailAsync(
                    command.FileStream,
                    cancellationToken: cancellationToken);

                var thumbnailFileName = $"thumb_{Path.GetFileNameWithoutExtension(command.FileName)}.jpg";
                var thumbnailStorageResult = await storageService.UploadAsync(
                    thumbnailResult.ThumbnailStream,
                    thumbnailFileName,
                    "image/jpeg",
                    cancellationToken);

                thumbnailPath = thumbnailStorageResult.StoragePath;
                thumbnailUrl = thumbnailStorageResult.PublicUrl;
                width = thumbnailResult.Width;
                height = thumbnailResult.Height;

                logger.LogInformation(
                    "Thumbnail generated for {FileName} by user {UserId}", 
                    command.FileName, 
                    uploadedBy);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to generate thumbnail for {FileName}", command.FileName);
                // Continue without thumbnail
            }
        }

        // Create attachment entity
        // Category is automatically calculated from EntityType + Type
        var attachment = AttachmentFile.Create(
            Guid.NewGuid(),
            command.FileName,
            command.FileName, // OriginalFileName
            command.ContentType,
            storageResult.FileSize,
            storageResult.StoragePath,
            storageResult.PublicUrl,
            command.Type,
            uploadedBy,
            command.EntityType,
            command.EntityId,
            command.Description,
            command.IsPublic);

        // Set thumbnail if generated
        if (thumbnailUrl != null && thumbnailPath != null && width.HasValue && height.HasValue)
        {
            attachment.SetThumbnail(thumbnailPath, thumbnailUrl, width.Value, height.Value);
        }

        // Save to database (audit fields set by AuditableEntityInterceptor)
        await repository.AddAsync(attachment, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "File uploaded successfully: {FileName} by user {UserId}, Storage: {StoragePath}",
            command.FileName,
            uploadedBy,
            storageResult.StoragePath);

        return new UploadAttachmentResult(
            attachment.Id,
            attachment.FileName,
            attachment.FileUrl,
            attachment.ThumbnailUrl,
            attachment.FileSize,
            attachment.ContentType);
    }
}
