using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shared.Contracts.Pagination;
using Shared.Core.Authorization;
using Shared.Core.Messaging.Outbox;
using Shared.Core.MultiTenancy.Web;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Api.Endpoints;

/// <summary>
/// Platform operators inspect and requeue dead-lettered outbox rows (see wiki/Architecture/Messaging.md). The context is
/// the DbContext type name (e.g. <c>CatalogDbContext</c>); for tenant-data contexts an optional <c>tenantId</c> targets
/// one tenant (needed for a tenant on a dedicated database), otherwise the shared database is used.
/// </summary>
public class OutboxAdminEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(EndpointConfig.BaseApiPath + "/admin/outbox")
            .WithApiVersionSet(app.NewApiVersionSet("Outbox").Build())
            .HasApiVersion(1.0)
            .WithTags("Outbox");

        group.MapGet("/{context}/dead-letters",
                async (string context, Guid? tenantId, int? page, int? pageSize, OutboxAdminService outbox,
                    CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(
                        await outbox.ListDeadLettersAsync(context, tenantId, page, pageSize, ct))))
            .RequirePermission(Permissions.OutboxManage)
            .WithName("GetOutboxDeadLetters")
            .Produces<ApiResponse<PaginatedResult<OutboxDeadLetterDto>>>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .WithSummary("List dead-lettered outbox messages of a context")
            .WithDescription("Oldest failures first. The payload is not returned; LastError tells why delivery was given up.");

        group.MapPost("/{context}/dead-letters/{id:guid}/requeue",
                async (string context, Guid id, Guid? tenantId, OutboxAdminService outbox, CancellationToken ct) =>
                {
                    await outbox.RequeueAsync(context, id, tenantId, ct);
                    return Results.Ok(ApiResponseExtensions.Success(new OutboxRequeueResult(1)));
                })
            .RequirePermission(Permissions.OutboxManage)
            .DisallowImpersonation()
            .WithName("RequeueOutboxDeadLetter")
            .Produces<ApiResponse<OutboxRequeueResult>>()
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .WithSummary("Requeue one dead-lettered outbox message")
            .WithDescription("Resets attempts and the dead-letter mark; the next outbox sweep redelivers it (at-least-once, consumers are idempotent). 409 when the row is not dead-lettered.");

        group.MapPost("/{context}/dead-letters/requeue",
                async (string context, Guid? tenantId, OutboxAdminService outbox, CancellationToken ct) =>
                    Results.Ok(ApiResponseExtensions.Success(
                        new OutboxRequeueResult(await outbox.RequeueAllAsync(context, tenantId, ct)))))
            .RequirePermission(Permissions.OutboxManage)
            .DisallowImpersonation()
            .WithName("RequeueOutboxDeadLetters")
            .Produces<ApiResponse<OutboxRequeueResult>>()
            .Produces<ApiResponse>(StatusCodes.Status404NotFound)
            .WithSummary("Requeue every dead-lettered outbox message of a context")
            .WithDescription("Same as the single requeue, for all dead letters of the context (and tenant, when given).");
    }
}

public sealed record OutboxRequeueResult(int Requeued);
