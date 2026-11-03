using System.Globalization;
using Attachment.Contracts.Attachments.Queries;
using Shared.Core.Response;

namespace Attachment.Domains.Attachments.Features.GetAttachmentsByEntity;

public class GetAttachmentsByEntityEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/attachments/entity/{entityType}/{entityId:guid}",
                async (string entityType, Guid entityId, int? pageNumber, int? pageSize, ISender sender,
                    HttpContext http, CancellationToken cancellationToken) =>
                {
                    if (!Enum.TryParse<EntityType>(entityType, true, out var parsed) || !Enum.IsDefined(parsed))
                    {
                        return Results.BadRequest(ApiResponseExtensions.BadRequest("Invalid entity type"));
                    }

                    var result = await sender.Send(
                        new GetAttachmentsByEntityQuery(parsed, entityId, pageNumber, pageSize), cancellationToken);

                    // The body stays a plain list (backwards compatible); paging metadata travels in headers.
                    http.Response.Headers["X-Total-Count"] = result.TotalCount.ToString(CultureInfo.InvariantCulture);
                    http.Response.Headers["X-Page-Number"] = result.PageNumber.ToString(CultureInfo.InvariantCulture);
                    http.Response.Headers["X-Page-Size"] = result.PageSize.ToString(CultureInfo.InvariantCulture);
                    return Results.Ok(ApiResponseExtensions.Success(result.Attachments));
                })
            .WithName("GetAttachmentsByEntity")
            .WithTags("Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<List<AttachmentDto>>>()
            .WithSummary("Get Attachments By Entity")
            .WithDescription(
                "Attachments of an entity (newest first): all of them when the entity type's access policy allows " +
                "reading, otherwise only public ones and the caller's own uploads. Optional pageNumber/pageSize " +
                "(default: first page of Paging:MaxPageSize); totals in X-Total-Count / X-Page-Number / X-Page-Size.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
