using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Attachment.Data.Repository.Attachments;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Domains.Attachments.Models;
using Attachment.Infrastructure.BackgroundJobs;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Storage;
using Attachment.Infrastructure.Validation;
using Attachment.Resources;
using Hangfire;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Attachment.Domains.Attachments.Features.UploadPendingAttachment;

public class UploadPendingAttachmentCommandValidator : AbstractValidator<UploadPendingAttachmentCommand>
{
    public UploadPendingAttachmentCommandValidator(IStringLocalizer<AttachmentResource> localizer)
    {
        RuleFor(x => x.SessionId)
            .NotEmpty().WithMessage(_ => localizer["Validation.SessionId.Required"]);

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage(_ => localizer["Validation.FileName.Required"])
            .MaximumLength(500).WithMessage(_ => localizer["Validation.FileName.MaxLength"]);

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage(_ => localizer["Validation.ContentType.Required"]);

        RuleFor(x => x.FileSize)
            .GreaterThan(0).WithMessage(_ => localizer["Validation.FileSize.Min"]);

        RuleFor(x => x.FileStream)
            .NotNull().WithMessage(_ => localizer["Validation.FileStream.Required"]);

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.DisplayOrder.Min"]);

        RuleFor(x => x.IsPrimary)
            .NotNull().WithMessage(_ => localizer["Validation.IsPrimary.Required"]);
    }
}

public class UploadPendingAttachmentHandler(
    IPendingAttachmentRepository repository,
    IStorageService storageService,
    IFileValidationService fileValidationService,
    IBackgroundJobClient backgroundJobClient,
    IOptions<AttachmentOptions> options,
    ILogger<UploadPendingAttachmentHandler> logger)
    : ICommandHandler<UploadPendingAttachmentCommand, UploadPendingAttachmentResult>
{
    private readonly AttachmentOptions _options = options.Value;

    public async Task<UploadPendingAttachmentResult> Handle(
        UploadPendingAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        // Check session upload limit
        var currentCount = await repository.GetCountBySessionIdAsync(command.SessionId, cancellationToken);
        if (currentCount >= _options.Pending.MaxUploadsPerSession)
        {
            throw new SessionLimitExceededException(command.SessionId, _options.Pending.MaxUploadsPerSession);
        }

        // Validate DisplayOrder uniqueness for session
        var existingAttachments = await repository.GetBySessionIdAsync(command.SessionId, cancellationToken);
        if (existingAttachments.Any(a => a.DisplayOrder == command.DisplayOrder && !a.IsDeleted))
        {
            throw new InvalidOperationException(
                $"Display order {command.DisplayOrder} already exists for session {command.SessionId}. " +
                "Please use a unique display order.");
        }

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
            throw new InvalidFileException(
                $"File validation failed: {string.Join(", ", validationResult.Errors)}");
        }

        // Generate unique file ID
        var fileId = Guid.NewGuid();
        var fileExtension = Path.GetExtension(command.FileName);
        var storagePath = $"temp/{command.SessionId}/{fileId}{fileExtension}";

        // Upload to temporary location
        var storageResult = await storageService.UploadToPathAsync(
            command.FileStream,
            storagePath,
            command.ContentType,
            cancellationToken);

        // Create pending attachment entity
        var pendingAttachment = PendingAttachment.Create(
            fileId,
            command.SessionId,
            Path.GetFileName(storageResult.StoragePath),
            command.FileName,
            command.ContentType,
            storageResult.FileSize,
            storageResult.StoragePath,
            storageResult.PublicUrl,
            command.Type,
            command.Category,
            command.DisplayOrder,
            command.IsPrimary,
            _options.Pending.ExpirationHours);

        // Save to database
        await repository.AddAsync(pendingAttachment, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        // Enqueue thumbnail generation job for images (async, non-blocking)
        if (command.Type == AttachmentType.Image && _options.Image.EnableThumbnailGeneration)
        {
            backgroundJobClient.Enqueue<GenerateThumbnailJob>(
                job => job.ExecuteAsync(fileId));

            logger.LogInformation(
                "Thumbnail generation job enqueued for pending attachment {FileId}",
                fileId);
        }

        logger.LogInformation(
            "Pending attachment uploaded: SessionId={SessionId}, FileId={FileId}, FileName={FileName}",
            command.SessionId,
            fileId,
            command.FileName);

        return new UploadPendingAttachmentResult(
            pendingAttachment.Id,
            pendingAttachment.SessionId,
            pendingAttachment.FileName,
            pendingAttachment.FileUrl,
            pendingAttachment.ExpiresAt,
            pendingAttachment.DisplayOrder);
    }
}
