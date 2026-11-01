using Attachment.Contracts.Attachments.Queries;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Attachment.Domains.Attachments.Features.GetPendingAttachmentsBySession;

public class GetPendingAttachmentsBySessionEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/attachments/pending/session/{sessionId:guid}",
                async (Guid sessionId, ISender sender, CancellationToken cancellationToken) =>
                {
                    var query = new GetPendingAttachmentsBySessionQuery(sessionId);
                    var result = await sender.Send(query, cancellationToken);

                    return Results.Ok(ApiResponseExtensions.Success(result, "Pending attachments retrieved successfully"));
                })
            .WithName("GetPendingAttachmentsBySession")
            .WithTags("Pending Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<GetPendingAttachmentsBySessionResult>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithSummary("Get all pending attachments for a session")
            .WithDescription("Retrieves all pending attachments for a session with remaining slots count")
            .WithOpenApi()
            .AllowAnonymous()
            .HasApiVersion(1.0);
    }
}
