using Attachment.Contracts.Attachments.Commands;
using Shared.Core.Response;

namespace Attachment.Domains.Attachments.Features.DeleteAttachment;

public record DeleteAttachmentResponse(bool Success);

public class DeleteAttachmentEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete(EndpointConfig.BaseApiPath + "/attachments/{id:guid}",
                async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(
                        new DeleteAttachmentCommand(id) { EnforceEntityAccessPolicy = true }, cancellationToken);

                    return Results.Ok(ApiResponseExtensions.Success(new DeleteAttachmentResponse(result.Success)));
                })
            .WithName("DeleteAttachment")
            .WithTags("Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<DeleteAttachmentResponse>>()
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .WithSummary("Delete Attachment")
            .WithDescription(
                "Deletes an attachment. Admins always; for an entity attachment the entity type's access policy " +
                "must allow the caller; an unlinked attachment can be deleted by its uploader.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
