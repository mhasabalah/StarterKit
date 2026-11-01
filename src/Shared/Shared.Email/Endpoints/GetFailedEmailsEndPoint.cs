using Shared.Contracts.CQRS;
using Shared.Contracts.Pagination;

namespace Shared.Email.Endpoints;

public record GetFailedEmailsRequest : BaseQuery;

public sealed class GetFailedEmailsEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(EndpointConfig.BaseApiPath + "/email/failed",
                async (
                    [AsParameters] GetFailedEmailsRequest request,
                    EmailLogRepository repository,
                    CancellationToken ct) =>
                {
                    var result = await repository.GetFailedPagedAsync(request.PageNumber ?? 1, request.PageSize ?? 10, ct);
                    return Results.Ok(ApiResponseExtensions.Success(result));
                })
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .RequireRateLimiting("search")
            .WithName("GetFailedEmails")
            .WithApiVersionSet(app.NewApiVersionSet("Email").Build())
            .WithTags("Email")
            .Produces<ApiResponse<PaginatedResult<FailedEmailDto>>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithSummary("Get Failed Emails")
            .WithDescription("Retrieve paginated list of emails that failed in the last 24 hours. Admin only.")
            .WithOpenApi()
            .HasApiVersion(1.0);
    }
}
