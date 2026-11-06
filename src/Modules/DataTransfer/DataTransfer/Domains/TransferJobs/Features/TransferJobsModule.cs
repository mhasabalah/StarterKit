using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Entitlements;
using Shared.Core.MultiTenancy.Web;
using Shared.Core.Web.Idempotency;

namespace DataTransfer.Domains.TransferJobs.Features;

public sealed class StartImportRequest
{
    [FromForm(Name = "file")]
    public required IFormFile File { get; init; }

    [FromForm(Name = "definitionKey")]
    public required string DefinitionKey { get; init; }

    /// <summary>Optional; <see cref="ImportMode.DryRun"/> when omitted. (Nullable: the form binder does not keep
    /// property initializers for missing fields.)</summary>
    [FromForm(Name = "mode")]
    public ImportMode? Mode { get; init; }

    /// <summary>Optional; <see cref="ImportPolicy.BestEffort"/> when omitted.</summary>
    [FromForm(Name = "policy")]
    public ImportPolicy? Policy { get; init; }

    /// <summary>Optional; derived from the file extension when omitted.</summary>
    [FromForm(Name = "format")]
    public TransferFormat? Format { get; init; }
}

public sealed record CommitImportRequest(ImportPolicy? Policy = null);

public sealed record StartExportRequest(
    string DefinitionKey,
    TransferFormat Format = TransferFormat.Xlsx,
    Dictionary<string, string>? Filter = null);

/// <summary>
/// Bulk CSV/Excel import and export (<c>/api/v1/data-transfer</c>). Starting a job returns 202 with the job; it runs
/// in the background (Hangfire queue <c>data-transfer</c>). Poll <c>GET /jobs/{id}</c> or wait for the
/// <c>datatransfer.job-finished</c> inbox notification. See wiki/Modules/DataTransfer.md.
/// </summary>
public class TransferJobsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/data-transfer")
            .WithTags("DataTransfer")
            .WithApiVersionSet(app.NewApiVersionSet("DataTransfer").Build())
            .HasApiVersion(1.0);

        MapDefinitions(group);
        MapStart(group, app.ServiceProvider.GetRequiredService<IOptions<DataTransferOptions>>().Value.Limits.MaxFileBytes);
        MapJobs(group);
    }

    private static void MapDefinitions(RouteGroupBuilder group)
    {
        group.MapGet("/definitions", async (ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetTransferDefinitionsQuery(), ct))))
            .WithName("GetTransferDefinitions").Produces<ApiResponse<IReadOnlyList<TransferDefinitionDto>>>()
            .WithSummary("Import/export definitions")
            .WithDescription("The definitions the caller may use, with their columns (localized headers, samples).")
            .RequireTenantMember();

        group.MapGet("/definitions/{key}/template",
                async (string key, TransferFormat? format, ISender sender, CancellationToken ct) =>
                {
                    var template = await sender.Send(new GetTransferTemplateQuery(key, format ?? TransferFormat.Xlsx), ct);
                    return Results.File(template.Content, template.ContentType, template.FileName);
                })
            .WithName("GetTransferTemplate")
            .Produces(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Import template")
            .WithDescription("An empty file (`format=xlsx|csv`) with the localized headers and one sample row.")
            .RequireTenantPermission(TenantPermissions.DataImport)
            .RequireFeature(EntitlementKeys.BulkImport);
    }

    private static void MapStart(RouteGroupBuilder group, long maxFileBytes)
    {
        group.MapPost("/imports", async ([FromForm] StartImportRequest request, ISender sender, CancellationToken ct) =>
                {
                    await using var stream = request.File.OpenReadStream();
                    var job = await sender.Send(new StartImportCommand(request.DefinitionKey, request.File.FileName,
                        request.File.ContentType, request.File.Length, stream, request.Mode ?? ImportMode.DryRun,
                        request.Policy ?? ImportPolicy.BestEffort,
                        request.Format), ct);
                    return Accepted(job);
                })
            .WithName("StartImport")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(maxFileBytes + (64 * 1024)))
            .WithFormOptions(multipartBodyLengthLimit: maxFileBytes + (64 * 1024))
            .Accepts<StartImportRequest>("multipart/form-data")
            .Produces<ApiResponse<TransferJobDto>>(StatusCodes.Status202Accepted)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .WithSummary("Start an import")
            .WithDescription(
                "Uploads a .csv or .xlsx file and queues an import. `mode=DryRun` (default) validates every row and writes " +
                "nothing; `Commit` writes. `policy=BestEffort` skips invalid rows, `ValidateFirst` writes nothing unless every " +
                "row is valid. One active import per definition and tenant (409 `import_already_running`).")
            .RequireTenantPermission(TenantPermissions.DataImport)
            .RequireFeature(EntitlementKeys.BulkImport)
            .DisallowImpersonation()
            .RequireRateLimiting("create");

        group.MapPost("/imports/{id:guid}/commit",
                async (Guid id, CommitImportRequest? request, ISender sender, CancellationToken ct) =>
                    Accepted(await sender.Send(new CommitImportCommand(id, request?.Policy), ct)))
            .WithName("CommitImport")
            .Produces<ApiResponse<TransferJobDto>>(StatusCodes.Status202Accepted)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status409Conflict)
            .WithSummary("Commit a dry run")
            .WithDescription("Queues a Commit import of the same file as a completed dry run (no second upload).")
            .RequireTenantPermission(TenantPermissions.DataImport)
            .RequireFeature(EntitlementKeys.BulkImport)
            .DisallowImpersonation()
            .WithIdempotency();

        group.MapPost("/exports", async (StartExportRequest request, ISender sender, CancellationToken ct) =>
                Accepted(await sender.Send(new StartExportCommand(request.DefinitionKey, request.Format, request.Filter), ct)))
            .WithName("StartExport")
            .Produces<ApiResponse<TransferJobDto>>(StatusCodes.Status202Accepted)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Start an export")
            .WithDescription("Queues an export (`format=Xlsx|Csv`) with an optional definition-specific `filter`.")
            .RequireTenantPermission(TenantPermissions.DataExport)
            .RequireFeature(EntitlementKeys.BulkExport)
            .RequireRateLimiting("create")
            .WithIdempotency();
    }

    private static void MapJobs(RouteGroupBuilder group)
    {
        group.MapGet("/jobs", async ([AsParameters] GetTransferJobsQuery query, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(query, ct))))
            .WithName("GetTransferJobs").Produces<ApiResponse<PaginatedResult<TransferJobDto>>>()
            .WithSummary("List transfer jobs")
            .WithDescription("Newest first: the caller's own jobs (every job with `tenant.audit.read`). Filters: kind, status, definitionKey.")
            .RequireTenantMember();

        group.MapGet("/jobs/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
                Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetTransferJobByIdQuery(id), ct))))
            .WithName("GetTransferJobById").Produces<ApiResponse<TransferJobDetailDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Transfer job")
            .WithDescription("Status, progress, counters and the first row errors.")
            .RequireTenantMember();

        group.MapGet("/jobs/{id:guid}/files/{kind}",
                async (Guid id, TransferJobFileKind kind, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new GetTransferJobFileQuery(id, kind), ct))))
            .WithName("GetTransferJobFile").Produces<ApiResponse<TransferJobFileResult>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Download a job file")
            .WithDescription("A short-lived signed URL of the `Source`, `Result` (export) or `ErrorReport` file.")
            .RequireTenantMember();

        group.MapPost("/jobs/{id:guid}/cancel",
                async (Guid id, ISender sender, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(await sender.Send(new CancelTransferJobCommand(id), ct))))
            .WithName("CancelTransferJob").Produces<ApiResponse<TransferJobDto>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Cancel a job")
            .WithDescription("Your own jobs only. A running job stops between two batches; committed batches stay committed.")
            .RequireTenantMember();
    }

    private static IResult Accepted(TransferJobDto job) =>
        Results.Accepted($"{EndpointConfig.BaseApiPath}/data-transfer/jobs/{job.Id}", ApiResponseExtensions.Success(job));
}
