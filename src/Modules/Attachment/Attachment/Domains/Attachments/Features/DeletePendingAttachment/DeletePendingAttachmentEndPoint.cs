using Attachment.Contracts.Attachments.Commands;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Attachment.Domains.Attachments.Features.DeletePendingAttachment;

public class DeletePendingAttachmentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/attachments/pending/{id:guid}",
                async (Guid id, Guid sessionId, ISender sender, CancellationToken cancellationToken) =>
                {
                    var command = new DeletePendingAttachmentCommand(id, sessionId);
                    var result = await sender.Send(command, cancellationToken);

                    return Results.Ok(ApiResponseExtensions.Success(
                        result,
                        "Pending attachment deleted successfully"));
                })
            .WithName("DeletePendingAttachment")
            .WithTags("Pending Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<bool>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithSummary("Delete a pending attachment")
            .WithDescription("Deletes a pending attachment from session. Requires sessionId query parameter for validation.")
            .WithOpenApi()
            .AllowAnonymous()
            .HasApiVersion(1.0);
    }
}
