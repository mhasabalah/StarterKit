using Attachment.Contracts.Attachments.Commands;
using Shared.Core.Response;

namespace Attachment.Domains.Attachments.Features.DeletePendingAttachment;

public class DeletePendingAttachmentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/attachments/pending/{id:guid}",
                async (Guid id, Guid sessionId, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new DeletePendingAttachmentCommand(id, sessionId), cancellationToken);

                    return Results.Ok(ApiResponseExtensions.Success(result, "Pending attachment deleted successfully"));
                })
            .WithName("DeletePendingAttachment")
            .WithTags("Pending Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<bool>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Delete a pending attachment")
            .WithDescription("Deletes a pending attachment. The sessionId query parameter must be a session owned by the caller.")
            .AllowAnonymous()
            .HasApiVersion(1.0);
    }
}
