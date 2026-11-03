using Attachment.Contracts.Attachments.Commands;
using Shared.Core.Response;

namespace Attachment.Domains.Attachments.Features.AssociatePendingAttachments;

public sealed record AssociatePendingAttachmentsRequest(
    Guid SessionId,
    EntityType EntityType,
    Guid EntityId,
    bool IsPublic = false);

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
                        ?? throw new UnauthorizedException("User must be authenticated");

                    var command = new AssociatePendingAttachmentsCommand(
                        request.SessionId,
                        request.EntityType,
                        request.EntityId,
                        userId,
                        request.IsPublic)
                    {
                        EnforceEntityAccessPolicy = true
                    };

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
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Associate pending attachments with an entity")
            .WithDescription(
                "Moves the files of an owned upload session to permanent storage and links them to an entity. " +
                "The entity type's attachment access policy must allow the caller.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
