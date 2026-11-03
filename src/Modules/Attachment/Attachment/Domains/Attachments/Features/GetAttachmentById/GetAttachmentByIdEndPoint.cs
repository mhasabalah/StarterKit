using Attachment.Contracts.Attachments.Queries;
using Shared.Core.Response;

namespace Attachment.Domains.Attachments.Features.GetAttachmentById;

public class GetAttachmentByIdEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/attachments/{id:guid}",
                async (Guid id, ISender sender, CancellationToken cancellationToken) =>
                {
                    var result = await sender.Send(new GetAttachmentByIdQuery(id), cancellationToken);
                    return Results.Ok(ApiResponseExtensions.Success(result.Attachment));
                })
            .WithName("GetAttachmentById")
            .WithTags("Attachments")
            .WithApiVersionSet(app.NewApiVersionSet("Attachment").Build())
            .Produces<ApiResponse<AttachmentDto>>()
            .WithSummary("Get Attachment By ID")
            .WithDescription("Retrieve the metadata of one attachment. FileUrl points at the access-checked download endpoint.")
            .RequireAuthorization()
            .HasApiVersion(1.0);
    }
}
