using System.Text.Json;
using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using DataTransfer.Domains.TransferJobs.Jobs;
using DataTransfer.Domains.TransferJobs.Services;
using Hangfire;
using Microsoft.Extensions.Options;

namespace DataTransfer.Domains.TransferJobs.Features;

// ----- start import ---------------------------------------------------------------------------------------------

public class StartImportCommandValidator : AbstractValidator<StartImportCommand>
{
    public StartImportCommandValidator(IStringLocalizer<DataTransferResource> localizer, IOptions<DataTransferOptions> options)
    {
        RuleFor(x => x.DefinitionKey)
            .NotEmpty().WithMessage(_ => localizer["Validation.DefinitionKey.Required"])
            .MaximumLength(TransferJob.DefinitionKeyMaxLength).WithMessage(_ => localizer["Validation.DefinitionKey.MaxLength"]);
        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage(_ => localizer["Validation.File.Required"])
            .MaximumLength(TransferJob.FileNameMaxLength).WithMessage(_ => localizer["Validation.FileName.MaxLength"]);
        RuleFor(x => x.FileSize)
            .GreaterThan(0).WithMessage(_ => localizer["Validation.File.Required"])
            .LessThanOrEqualTo(options.Value.Limits.MaxFileBytes).WithMessage(_ => localizer["Validation.File.TooLarge"]);
        RuleFor(x => x.Mode).IsInEnum().WithMessage(_ => localizer["Validation.Mode.Invalid"]);
        RuleFor(x => x.Policy).IsInEnum().WithMessage(_ => localizer["Validation.Policy.Invalid"]);
        RuleFor(x => x.Format).IsInEnum().WithMessage(_ => localizer["Validation.Format.Invalid"]).When(x => x.Format.HasValue);
    }
}

/// <summary>
/// Stores the file through Attachment (content validation by magic bytes and the malware scan, fail-closed), then
/// creates the job and queues it on <c>data-transfer</c>. If the row cannot be saved, the stored file is removed again.
/// </summary>
public class StartImportHandler(
    ITransferJobRepository repository,
    DataTransferDbContext dbContext,
    TransferDefinitionRegistry definitions,
    TransferAccess access,
    ISender sender,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider,
    ILogger<StartImportHandler> logger) : ICommandHandler<StartImportCommand, TransferJobDto>
{
    public async Task<TransferJobDto> Handle(StartImportCommand command, CancellationToken cancellationToken)
    {
        var definition = definitions.GetImport(command.DefinitionKey);
        await access.EnsureAsync(definition.RequiredPermission);
        var format = command.Format ?? TransferFormats.FromFileName(command.FileName);

        if (await dbContext.Jobs.AnyAsync(j => j.Kind == TransferJobKind.Import && j.DefinitionKey == definition.Key &&
                                               (j.Status == TransferJobStatus.Pending || j.Status == TransferJobStatus.Running),
                cancellationToken))
        {
            throw new ImportAlreadyRunningException(definition.Key);
        }

        var jobId = Guid.CreateVersion7();
        var upload = await sender.Send(new UploadAttachmentCommand(
            command.FileName, command.ContentType, command.FileSize, command.FileStream, AttachmentType.Document,
            EntityType.DataTransferJob, jobId, IsPublic: false, GenerateThumbnail: false, UploadedBy: access.UserId),
            cancellationToken);

        var job = TransferJob.CreateImport(jobId, definition.Key, format, command.Mode, command.Policy,
            command.FileName, upload.Id, access.UserId, TransferAccess.Culture);
        job.MarkEnqueued(timeProvider.GetUtcNow().UtcDateTime);

        try
        {
            await repository.AddAsync(job, cancellationToken);
        }
        catch
        {
            await TransferFiles.TryDeleteAsync(sender, jobId, logger);
            throw;
        }

        TransferJobQueue.Enqueue(jobs, job.Id, job.Kind);
        logger.LogInformation("Import {JobId} of {Definition} queued ({Mode}, {Policy})", job.Id, job.DefinitionKey,
            job.Mode, job.Policy);
        return TransferJobProjections.Map(job);
    }
}

// ----- commit a dry run -----------------------------------------------------------------------------------------

public class CommitImportCommandValidator : AbstractValidator<CommitImportCommand>
{
    public CommitImportCommandValidator(IStringLocalizer<DataTransferResource> localizer)
    {
        RuleFor(x => x.DryRunJobId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Policy).IsInEnum().WithMessage(_ => localizer["Validation.Policy.Invalid"]).When(x => x.Policy.HasValue);
    }
}

public class CommitImportHandler(
    ITransferJobRepository repository,
    TransferDefinitionRegistry definitions,
    TransferAccess access,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider) : ICommandHandler<CommitImportCommand, TransferJobDto>
{
    public async Task<TransferJobDto> Handle(CommitImportCommand command, CancellationToken cancellationToken)
    {
        var dryRun = await repository.GetByIdAsync(command.DryRunJobId, cancellationToken)
                     ?? throw new TransferJobNotFoundException(command.DryRunJobId);
        access.EnsureOwner(dryRun);
        await access.EnsureAsync(definitions.GetImport(dryRun.DefinitionKey).RequiredPermission);

        var job = TransferJob.CreateCommitFrom(dryRun, command.Policy, access.UserId, TransferAccess.Culture);
        job.MarkEnqueued(timeProvider.GetUtcNow().UtcDateTime);
        await repository.AddAsync(job, cancellationToken);

        TransferJobQueue.Enqueue(jobs, job.Id, job.Kind);
        return TransferJobProjections.Map(job);
    }
}

// ----- start export ---------------------------------------------------------------------------------------------

public class StartExportCommandValidator : AbstractValidator<StartExportCommand>
{
    public const int MaxFilterEntries = 20;

    public StartExportCommandValidator(IStringLocalizer<DataTransferResource> localizer)
    {
        RuleFor(x => x.DefinitionKey)
            .NotEmpty().WithMessage(_ => localizer["Validation.DefinitionKey.Required"])
            .MaximumLength(TransferJob.DefinitionKeyMaxLength).WithMessage(_ => localizer["Validation.DefinitionKey.MaxLength"]);
        RuleFor(x => x.Format).IsInEnum().WithMessage(_ => localizer["Validation.Format.Invalid"]);
        RuleFor(x => x.Filter)
            .Must(f => f!.Count <= MaxFilterEntries).WithMessage(_ => localizer["Validation.Filter.TooLarge"])
            .When(x => x.Filter is not null);
    }
}

public class StartExportHandler(
    ITransferJobRepository repository,
    TransferDefinitionRegistry definitions,
    TransferAccess access,
    IBackgroundJobClient jobs,
    TimeProvider timeProvider) : ICommandHandler<StartExportCommand, TransferJobDto>
{
    public async Task<TransferJobDto> Handle(StartExportCommand command, CancellationToken cancellationToken)
    {
        var definition = definitions.GetExport(command.DefinitionKey);
        await access.EnsureAsync(definition.RequiredPermission);

        var filter = command.Filter is { Count: > 0 } ? JsonSerializer.Serialize(command.Filter) : null;
        var job = TransferJob.CreateExport(definition.Key, command.Format, filter, access.UserId, TransferAccess.Culture);
        job.MarkEnqueued(timeProvider.GetUtcNow().UtcDateTime);
        await repository.AddAsync(job, cancellationToken);

        TransferJobQueue.Enqueue(jobs, job.Id, job.Kind);
        return TransferJobProjections.Map(job);
    }
}

// ----- cancel ---------------------------------------------------------------------------------------------------

public class CancelTransferJobCommandValidator : AbstractValidator<CancelTransferJobCommand>
{
    public CancelTransferJobCommandValidator(IStringLocalizer<DataTransferResource> localizer) =>
        RuleFor(x => x.JobId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

/// <summary>Pending: cancelled at once. Running: the worker stops between two batches. Repeating it is harmless.</summary>
public class CancelTransferJobHandler(
    ITransferJobRepository repository,
    TransferAccess access,
    TimeProvider timeProvider,
    IOptions<DataTransferOptions> options) : ICommandHandler<CancelTransferJobCommand, TransferJobDto>
{
    public async Task<TransferJobDto> Handle(CancelTransferJobCommand command, CancellationToken cancellationToken)
    {
        var job = await repository.GetByIdAsync(command.JobId, cancellationToken)
                  ?? throw new TransferJobNotFoundException(command.JobId);
        access.EnsureOwner(job);
        if (job.CancelRequested)
            return TransferJobProjections.Map(job);

        job.RequestCancel(timeProvider.GetUtcNow().UtcDateTime, options.Value.RetentionDays);
        await repository.SaveChangesAsync(cancellationToken);
        return TransferJobProjections.Map(job);
    }
}

// ----- helpers --------------------------------------------------------------------------------------------------

public static class TransferFormats
{
    public static TransferFormat FromFileName(string fileName) =>
        Path.GetExtension(fileName).ToUpperInvariant() switch
        {
            ".CSV" or ".TXT" => TransferFormat.Csv,
            ".XLSX" => TransferFormat.Xlsx,
            _ => throw new InvalidTransferFileException("unsupported_format")
        };
}

internal static class TransferFiles
{
    /// <summary>Best-effort cleanup of a job's stored files after its row could not be saved.</summary>
    public static async Task TryDeleteAsync(ISender sender, Guid jobId, ILogger logger)
    {
        try
        {
            await sender.Send(new DeleteAttachmentsByEntityCommand(EntityType.DataTransferJob, jobId), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Files of the unsaved transfer job {JobId} could not be removed", jobId);
        }
    }
}
