using System.Globalization;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;

namespace DataTransfer.Domains.TransferJobs.Services;

/// <summary>
/// Who the HTTP caller is and what they may do with transfer jobs.
/// <list type="bullet">
/// <item>Starting a job needs the DataTransfer permission (endpoint policy) AND the definition's own
/// <see cref="ITransferDefinition.RequiredPermission"/> (checked here, through the same tenant policies, so API-key
/// scopes and single-tenant platform permissions apply exactly as on endpoints).</item>
/// <item>Jobs are private to their requester; holders of <see cref="TenantPermissions.AuditRead"/> may read
/// (list, detail, download) every job of the tenant. Only the requester may cancel or commit.</item>
/// </list>
/// </summary>
public sealed class TransferAccess(
    IHttpContextAccessor httpContextAccessor,
    IHttpContextService httpContext,
    IAuthorizationService authorization)
{
    public Guid UserId => httpContext.GetUserIdAsGuid()
                          ?? throw new UnauthorizedException(TransferText.Get("Transfer.Error.authentication_required"));

    /// <summary><c>ar</c> or <c>en</c>, from the request culture.</summary>
    public static string Culture => TransferCulture.Normalize(CultureInfo.CurrentUICulture.Name);

    public async Task<bool> HasAsync(string tenantPermission)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null)
            return false;
        var result = await authorization.AuthorizeAsync(user, TenantPermissions.PolicyName(tenantPermission));
        return result.Succeeded;
    }

    public async Task EnsureAsync(string tenantPermission)
    {
        if (!await HasAsync(tenantPermission))
            throw new ForbiddenException(TransferText.Get("Transfer.Error.permission_required", tenantPermission));
    }

    /// <summary>Read filter: the caller's own jobs, or every job for auditors.</summary>
    public async Task<Expression<Func<TransferJob, bool>>> ReadableAsync()
    {
        if (await HasAsync(TenantPermissions.AuditRead))
            return _ => true;
        var userId = UserId;
        return job => job.RequestedBy == userId;
    }

    /// <summary>Only the requester acts on a job. Someone else's job is reported as not found (no id probing).</summary>
    public void EnsureOwner(TransferJob job)
    {
        if (job.RequestedBy != UserId)
            throw new TransferJobNotFoundException(job.Id);
    }
}

/// <summary>SQL projections of jobs.</summary>
public static class TransferJobProjections
{
    public static readonly Expression<Func<TransferJob, TransferJobDto>> ToDto = j => new TransferJobDto(
        j.Id, j.Kind, j.DefinitionKey, j.Format, j.Mode, j.Policy, j.Status, j.FileName, j.TotalRows,
        j.ProcessedRows, j.CreatedRows, j.UpdatedRows, j.UnchangedRows, j.FailedRows, j.CancelRequested,
        j.ResultAttachmentId != null, j.ErrorReportAttachmentId != null, j.FailureCode, j.FailureMessage,
        j.RequestedBy, j.CreatedAt ?? DateTime.MinValue, j.StartedAt, j.FinishedAt, j.ExpiresAt, j.Version);

    private static readonly Func<TransferJob, TransferJobDto> Compiled = ToDto.Compile();

    public static TransferJobDto Map(TransferJob job) => Compiled(job);
}
