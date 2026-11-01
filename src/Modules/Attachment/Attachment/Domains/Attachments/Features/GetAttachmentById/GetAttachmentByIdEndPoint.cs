using Attachment.Contracts.Attachments.Queries;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Attachment.Domains.Attachments.Features.GetAttachmentById;

public class GetAttachmentByIdEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/attachments/{id:guid}", async (Guid id, [FromServices] ISender sender) =>
            {
                var query = new GetAttachmentByIdQuery(id);
                var result = await sender.Send(query);

                return Results.Ok(ApiResponseExtensions.Success(result.Attachment));
            })
            .WithName("GetAttachmentById")
            .WithTags("Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<AttachmentDto>>()
            .WithSummary("Get Attachment By ID")
            .WithDescription("Retrieve a single attachment by its ID")
            .WithOpenApi()
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
