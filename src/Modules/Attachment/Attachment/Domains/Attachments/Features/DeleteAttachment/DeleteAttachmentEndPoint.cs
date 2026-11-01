using Attachment.Contracts.Attachments.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Attachment.Domains.Attachments.Features.DeleteAttachment;

public record DeleteAttachmentResponse(bool Success);

public class DeleteAttachmentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/attachments/{id:guid}", async (Guid id, [FromServices] ISender sender) =>
            {
                var command = new DeleteAttachmentCommand(id);
                var result = await sender.Send(command);
                var response = result.Adapt<DeleteAttachmentResponse>();

                return Results.Ok(ApiResponseExtensions.Success(response));
            })
            .WithName("DeleteAttachment")
            .WithTags("Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<DeleteAttachmentResponse>>()
            .WithSummary("Delete Attachment")
            .WithDescription("Delete an attachment by ID")
            .WithOpenApi()
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
