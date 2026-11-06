using Attachment.Contracts.Attachments.Queries;
using DataTransfer.Domains.TransferJobs.Jobs;
using DataTransfer.Domains.TransferJobs.Services;
using Microsoft.Extensions.Options;

namespace DataTransfer.Domains.TransferJobs.Features;

// ----- list -----------------------------------------------------------------------------------------------------

public class GetTransferJobsQueryValidator : AbstractValidator<GetTransferJobsQuery>
{
    public GetTransferJobsQueryValidator(IStringLocalizer<DataTransferResource> localizer)
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => localizer["Validation.PageSize.Range"])
            .When(x => x.PageSize.HasValue);
        RuleFor(x => x.Kind).IsInEnum().When(x => x.Kind.HasValue);
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.DefinitionKey).MaximumLength(TransferJob.DefinitionKeyMaxLength);
    }
}

public class GetTransferJobsHandler(DataTransferDbContext dbContext, TransferAccess access)
    : IQueryHandler<GetTransferJobsQuery, PaginatedResult<TransferJobDto>>
{
    public async Task<PaginatedResult<TransferJobDto>> Handle(GetTransferJobsQuery query, CancellationToken cancellationToken)
    {
        var jobs = dbContext.Jobs.AsNoTracking().Where(await access.ReadableAsync());
        if (query.Kind is { } kind)
            jobs = jobs.Where(j => j.Kind == kind);
        if (query.Status is { } status)
            jobs = jobs.Where(j => j.Status == status);
        if (!string.IsNullOrWhiteSpace(query.DefinitionKey))
            jobs = jobs.Where(j => j.DefinitionKey == query.DefinitionKey);

        return await jobs
            .OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
            .Select(TransferJobProjections.ToDto)
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}

// ----- detail ---------------------------------------------------------------------------------------------------

public class GetTransferJobByIdHandler(
    DataTransferDbContext dbContext,
    TransferAccess access,
    IOptions<DataTransferOptions> options) : IQueryHandler<GetTransferJobByIdQuery, TransferJobDetailDto>
{
    public async Task<TransferJobDetailDto> Handle(GetTransferJobByIdQuery query, CancellationToken cancellationToken)
    {
        var job = await dbContext.Jobs.AsNoTracking()
                      .Where(await access.ReadableAsync())
                      .Where(j => j.Id == query.JobId)
                      .Select(TransferJobProjections.ToDto)
                      .FirstOrDefaultAsync(cancellationToken)
                  ?? throw new TransferJobNotFoundException(query.JobId);

        var errors = await dbContext.Errors.AsNoTracking()
            .Where(e => e.JobId == query.JobId)
            .OrderBy(e => e.RowNumber).ThenBy(e => e.Id)
            .Take(options.Value.ErrorPreviewCount)
            .Select(e => new TransferJobErrorDto(e.RowNumber, e.Column, e.Code, e.Message))
            .ToListAsync(cancellationToken);

        return new TransferJobDetailDto(job, errors);
    }
}

// ----- file download --------------------------------------------------------------------------------------------

public class GetTransferJobFileQueryValidator : AbstractValidator<GetTransferJobFileQuery>
{
    public GetTransferJobFileQueryValidator(IStringLocalizer<DataTransferResource> localizer)
    {
        RuleFor(x => x.JobId).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        RuleFor(x => x.Kind).IsInEnum().WithMessage(_ => localizer["Validation.FileKind.Invalid"]);
    }
}

/// <summary>
/// A short-lived signed URL of the source, result or error report (access is checked here; the Attachment query is
/// trusted). The requester may always download their files. Anyone else who can read the job (an auditor) ALSO needs
/// the definition's permission: the files hold the data itself, not just the job's metadata.
/// </summary>
public class GetTransferJobFileHandler(
    DataTransferDbContext dbContext,
    TransferAccess access,
    TransferDefinitionRegistry definitions,
    ISender sender) : IQueryHandler<GetTransferJobFileQuery, TransferJobFileResult>
{
    public async Task<TransferJobFileResult> Handle(GetTransferJobFileQuery query, CancellationToken cancellationToken)
    {
        var files = await dbContext.Jobs.AsNoTracking()
                        .Where(await access.ReadableAsync())
                        .Where(j => j.Id == query.JobId)
                        .Select(j => new
                        {
                            j.RequestedBy,
                            j.DefinitionKey,
                            j.SourceAttachmentId,
                            j.ResultAttachmentId,
                            j.ErrorReportAttachmentId
                        })
                        .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new TransferJobNotFoundException(query.JobId);

        if (files.RequestedBy != access.UserId)
            await access.EnsureAsync(definitions.Get(files.DefinitionKey).RequiredPermission);

        var attachmentId = query.Kind switch
        {
            TransferJobFileKind.Source => files.SourceAttachmentId,
            TransferJobFileKind.Result => files.ResultAttachmentId,
            _ => files.ErrorReportAttachmentId
        } ?? throw new TransferJobFileNotFoundException(query.Kind);

        var url = await sender.Send(new GetAttachmentReadUrlQuery(attachmentId), cancellationToken);
        return new TransferJobFileResult(url.Url, url.ExpiresAt, url.FileName, url.ContentType);
    }
}

// ----- definitions + templates ----------------------------------------------------------------------------------

/// <summary>The definitions the caller may use (DataTransfer permission AND the definition's own permission).</summary>
public class GetTransferDefinitionsHandler(TransferDefinitionRegistry definitions, TransferAccess access)
    : IQueryHandler<GetTransferDefinitionsQuery, IReadOnlyList<TransferDefinitionDto>>
{
    public async Task<IReadOnlyList<TransferDefinitionDto>> Handle(GetTransferDefinitionsQuery query,
        CancellationToken cancellationToken)
    {
        var canImport = await access.HasAsync(TenantPermissions.DataImport);
        var canExport = await access.HasAsync(TenantPermissions.DataExport);
        var importKeys = definitions.Imports.Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exportKeys = definitions.Exports.Select(d => d.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<TransferDefinitionDto>();
        foreach (var key in importKeys.Union(exportKeys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            var definition = definitions.Get(key);
            var import = canImport && importKeys.Contains(key);
            var export = canExport && exportKeys.Contains(key);
            if ((import || export) && await access.HasAsync(definition.RequiredPermission))
                result.Add(ToDto(definition, import, export));
        }

        return result;
    }

    private static TransferDefinitionDto ToDto(ITransferDefinition definition, bool import, bool export) =>
        new(definition.Key, definition.DisplayName, import, export,
            definition.Columns.Select(c => new TransferColumnDto(c.Name, c.Header, c.Required, c.Sample, c.Description, c.Kind))
                .ToList());
}

public class GetTransferTemplateQueryValidator : AbstractValidator<GetTransferTemplateQuery>
{
    public GetTransferTemplateQueryValidator(IStringLocalizer<DataTransferResource> localizer)
    {
        RuleFor(x => x.DefinitionKey).NotEmpty().WithMessage(_ => localizer["Validation.DefinitionKey.Required"])
            .MaximumLength(TransferJob.DefinitionKeyMaxLength).WithMessage(_ => localizer["Validation.DefinitionKey.MaxLength"]);
        RuleFor(x => x.Format).IsInEnum().WithMessage(_ => localizer["Validation.Format.Invalid"]);
    }
}

/// <summary>Localized headers (required ones marked <c>*</c>, still matched on import) and one sample row.</summary>
public class GetTransferTemplateHandler(TransferDefinitionRegistry definitions, TransferAccess access)
    : IQueryHandler<GetTransferTemplateQuery, TransferTemplateResult>
{
    public async Task<TransferTemplateResult> Handle(GetTransferTemplateQuery query, CancellationToken cancellationToken)
    {
        var definition = definitions.GetImport(query.DefinitionKey);
        await access.EnsureAsync(definition.RequiredPermission);

        var culture = TransferAccess.Culture;
        using var buffer = new MemoryStream();
        await using (var writer = TabularWriter.Create(buffer, query.Format, definition.Columns.Select(c => c.Kind).ToList(),
                         culture))
        {
            await writer.WriteRowAsync(definition.Columns
                .Select(c => (string?)(c.HeaderFor(culture) + (c.Required ? " *" : string.Empty)))
                .ToList(), cancellationToken);
            await writer.WriteRowAsync(definition.Columns.Select(c => c.Sample).ToList(), cancellationToken);
            await writer.CompleteAsync(cancellationToken);
        }

        return new TransferTemplateResult(buffer.ToArray(),
            $"{definition.Key}-template{TabularWriter.Extension(query.Format)}", TabularWriter.ContentType(query.Format));
    }
}
