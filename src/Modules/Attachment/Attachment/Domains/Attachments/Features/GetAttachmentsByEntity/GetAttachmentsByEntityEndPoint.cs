using Attachment.Contracts.Attachments.Queries;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Attachment.Domains.Attachments.Features.GetAttachmentsByEntity;

public class GetAttachmentsByEntityEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/attachments/entity/{entityType}/{entityId:guid}",
                async (string entityType, Guid entityId, [FromServices] ISender sender) =>
                {
                    if (!Enum.TryParse<EntityType>(entityType, true, out var parsedEntityType))
                    {
                        return Results.BadRequest(new { error = "Invalid entity type" });
                    }

                    var query = new GetAttachmentsByEntityQuery(parsedEntityType, entityId);
                    var result = await sender.Send(query);

                    return Results.Ok(ApiResponseExtensions.Success(result.Attachments));
                })
            .WithName("GetAttachmentsByEntity")
            .WithTags("Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<List<AttachmentDto>>>()
            .WithSummary("Get Attachments By Entity")
            .WithDescription("Retrieve all attachments for a specific entity")
            .WithOpenApi()
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
