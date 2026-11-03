using Shared.Core.MultiTenancy;
using Attachment.Contracts.Attachments.Commands;
using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.BackgroundJobs;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Security;
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
    }
}

/// <summary>
/// Uploads into a server-issued session owned by the caller. The session is locked (advisory lock) for the
/// duration so the per-session limit and display-order uniqueness hold under concurrent uploads.
/// </summary>
public class UploadPendingAttachmentHandler(
    IPendingAttachmentRepository repository,
    IStorageService storageService,
    IFileValidationService fileValidationService,
    IPendingSessionService sessionService,
    IBackgroundJobClient backgroundJobClient,
    IOptions<AttachmentOptions> options,
    ITenantContext tenantContext,
    ILogger<UploadPendingAttachmentHandler> logger)
    : ICommandHandler<UploadPendingAttachmentCommand, UploadPendingAttachmentResult>
{
    private readonly AttachmentOptions _options = options.Value;

    public async Task<UploadPendingAttachmentResult> Handle(
        UploadPendingAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        if (!sessionService.IsOwnedByCurrentCaller(command.SessionId))
            throw new PendingSessionAccessDeniedException();

        await using var upload = await SeekableUpload.CreateAsync(command.FileStream, cancellationToken);

        // Validate before taking the lock: it reads the whole file.
        var validation = await fileValidationService.ValidateFileAsync(
            upload.Stream, command.FileName, command.ContentType, command.FileSize, command.Type, cancellationToken);

        if (!validation.IsValid || validation.Detected is not { } detected)
            throw new InvalidFileException($"File validation failed: {string.Join(", ", validation.Errors)}");

        var contentHash = await ContentHasher.Sha256HexAsync(upload.Stream, cancellationToken);

        await using var sessionLock = await repository.BeginSessionLockAsync(command.SessionId, cancellationToken);

        var currentCount = await repository.GetCountBySessionIdAsync(command.SessionId, cancellationToken);
        if (currentCount >= _options.Pending.MaxUploadsPerSession)
            throw new SessionLimitExceededException(command.SessionId, _options.Pending.MaxUploadsPerSession);

        if (await repository.DisplayOrderExistsAsync(command.SessionId, command.DisplayOrder, cancellationToken))
            throw new DisplayOrderConflictException(command.DisplayOrder);

        var fileId = Guid.NewGuid();
        var storagePath = StoragePaths.ForPending(
            tenantContext.TenantId ?? throw new TenantNotResolvedException("upload"), command.SessionId, fileId, detected.Extension);

        upload.Stream.Position = 0;
        var stored = await storageService.UploadToPathAsync(
            upload.Stream, storagePath, detected.ContentType, cancellationToken);

        PendingAttachment pendingAttachment;
        try
        {
            pendingAttachment = PendingAttachment.Create(
                fileId,
                command.SessionId,
                Path.GetFileName(stored.StoragePath),
                validation.SafeFileName,
                detected.ContentType,
                stored.FileSize,
                stored.StoragePath,
                $"pending:{fileId:N}", // URLs are generated per request; the path is the source of truth
                detected.Type,
                command.Category,
                command.DisplayOrder,
                command.IsPrimary,
                _options.Pending.ExpirationHours,
                contentHash);

            await repository.AddAsync(pendingAttachment, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            await sessionLock.CommitAsync(cancellationToken);
        }
        catch
        {
            var cleanup = await storageService.DeleteBatchAsync([stored.StoragePath], CancellationToken.None);
            if (!cleanup.AllSucceeded)
                logger.LogError("Pending upload failed and blob {Path} could not be removed", stored.StoragePath);
            throw;
        }

        if (detected.Type == AttachmentType.Image && _options.Image.EnableThumbnailGeneration)
        {
            backgroundJobClient.Enqueue<GenerateThumbnailJob>(job => job.ExecuteAsync(fileId));
        }

        logger.LogInformation(
            "Pending attachment uploaded: SessionId={SessionId}, FileId={FileId}", command.SessionId, fileId);

        var readUrl = await storageService.GetReadUrlAsync(
            stored.StoragePath,
            TimeSpan.FromMinutes(_options.Download.ReadUrlTtlMinutes),
            new ReadUrlOptions(validation.SafeFileName, detected.ContentType,
                FileResponseHeaders.IsInline(detected.ContentType, _options.Download.InlineContentTypes)),
            cancellationToken);

        return new UploadPendingAttachmentResult(
            pendingAttachment.Id,
            pendingAttachment.SessionId,
            pendingAttachment.OriginalFileName,
            readUrl,
            pendingAttachment.ExpiresAt,
            pendingAttachment.DisplayOrder);
    }
}
