using Attachment.Contracts.Attachments.Queries;
using Shared.Core.Response;

namespace Attachment.Domains.Attachments.Features.GetPendingAttachmentsBySession;

public class GetPendingAttachmentsBySessionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/attachments/pending/session/{sessionId:guid}",
                async (Guid sessionId, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new GetPendingAttachmentsBySessionQuery(sessionId), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result, "Pending attachments retrieved successfully"));
                })
            .WithName("GetPendingAttachmentsBySession")
            .WithTags("Pending Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<GetPendingAttachmentsBySessionResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Get all pending attachments for a session")
            .WithDescription("Pending attachments of a session owned by the caller, with short-lived preview URLs.")
            .AllowAnonymous()
            .HasApiVersion(1.0);
    }
}
