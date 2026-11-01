namespace Shared.Email.Endpoints;

public sealed class ReEnqueueEmailEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(EndpointConfig.BaseApiPath + "/email/{id:guid}/reenqueue",
                async (Guid id, IEmailJobEnqueuer enqueuer, CancellationToken ct) =>
                {
                    try
                    {
                        var newJobId = await enqueuer.ReEnqueueAsync(id, ct);
                        return Results.Ok(ApiResponseExtensions.Success(new
                        {
                            EmailLogId = id,
                            NewJobId = newJobId,
                            Message = "Re-enqueued successfully"
                        }));
                    }
                    catch (NotFoundException ex)
                    {
                        return ApiResponseExtensions.NotFound(ex.Message);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return ApiResponseExtensions.BadRequest(ex.Message);
                    }
                })
            .WithName("ReEnqueueEmail")
            .WithApiVersionSet(app.NewApiVersionSet("Email").Build())
            .WithTags("Email")
            .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse<object>>(StatusCodes.Status404NotFound)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse<object>>(StatusCodes.Status403Forbidden)
            .WithSummary("Re-enqueue failed email")
            .WithDescription("Re-enqueue a failed email by email log id. Admin only.")
            .WithOpenApi()
            .RequireAuthorization(PolicyConstants.AdminOnly)
            .RequireRateLimiting("create")
            .HasApiVersion(1.0);
    }
}
