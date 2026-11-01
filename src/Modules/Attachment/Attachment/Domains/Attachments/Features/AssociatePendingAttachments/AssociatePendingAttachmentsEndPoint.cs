using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Attachment.Domains.Attachments.Features.AssociatePendingAttachments;

public sealed record AssociatePendingAttachmentsRequest(
    Guid SessionId,
    EntityType EntityType,
    Guid EntityId);

public class AssociatePendingAttachmentsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/attachments/pending/associate",
                async (AssociatePendingAttachmentsRequest request,
                       ISender sender,
                       IHttpContextService httpContextService,
                       CancellationToken cancellationToken) =>
                {
                    var userId = httpContextService.GetUserIdAsGuid()
                        ?? throw new UnauthorizedAccessException("User must be authenticated");

                    var command = new AssociatePendingAttachmentsCommand(
                        request.SessionId,
                        request.EntityType,
                        request.EntityId,
                        userId);

                    var result = await sender.Send(command, cancellationToken);

                    return Results.Ok(ApiResponseExtensions.Success(
                        result,
                        $"Successfully associated {result.TotalAssociated} attachments"));
                })
            .WithName("AssociatePendingAttachments")
            .WithTags("Pending Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<AssociatePendingAttachmentsResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithSummary("Associate pending attachments with an entity")
            .WithDescription("Moves pending attachments from session to permanent storage and links them to an entity")
            .WithOpenApi()
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
