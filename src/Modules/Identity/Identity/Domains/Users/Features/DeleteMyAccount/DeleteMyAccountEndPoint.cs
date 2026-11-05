using Shared.Core.Authorization;
using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Users.Commands;
using Microsoft.AspNetCore.Mvc;
using Shared.Core.MultiTenancy.Web;
using Shared.Core.Response;
using Shared.Core.Web;

namespace Identity.Domains.Users.Features.DeleteMyAccount;

public class DeleteMyAccountEndPoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var versionSet = app.NewApiVersionSet("Identity").Build();

        app.MapDelete("api/identity/profile", async (
                [FromBody] DeleteMyAccountRequest request,
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var userId = httpContextService.GetUserIdAsGuid();
                if (userId is null)
                    return Results.Unauthorized();

                var result = await sender.Send(
                    new DeleteMyAccountCommand(userId.Value, request.Password, request.OtpCode), cancellationToken);
                return Results.Ok(ApiResponse.Success(result, result.Message));
            })
            .RequireAuthorization()
            .DisallowImpersonation()
            .RequireRateLimiting("auth")
            .DisallowApiKey()
            .WithName("DeleteMyAccount")
            .WithApiVersionSet(versionSet)
            .WithTags("Profile")
            .WithSummary("Delete my account (irreversible)")
            .WithDescription(
                "Erases the signed-in user's account: ends all sessions and anonymizes the profile; other modules delete " +
                "the user's data asynchronously. Re-authenticates with the current password, or with an SMS code " +
                "(POST api/identity/profile/delete-code) for accounts without a password. 409 while the user owns a " +
                "tenant (transfer ownership first) or holds a privileged role.")
            .Produces<ApiResponse<DeleteMyAccountResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status403Forbidden)
            .Produces<ApiResponse>(StatusCodes.Status409Conflict)
            .HasApiVersion(1.0);

        app.MapPost("api/identity/profile/delete-code", async (
                ISender sender,
                IHttpContextService httpContextService,
                CancellationToken cancellationToken) =>
            {
                var userId = httpContextService.GetUserIdAsGuid();
                if (userId is null)
                    return Results.Unauthorized();

                var result = await sender.Send(new SendAccountDeletionCodeCommand(userId.Value), cancellationToken);
                return Results.Ok(ApiResponse.Success(result, result.Message));
            })
            .RequireAuthorization()
            .DisallowImpersonation()
            .RequireRateLimiting("otp")
            .DisallowApiKey()
            .WithName("SendAccountDeletionCode")
            .WithApiVersionSet(versionSet)
            .WithTags("Profile")
            .WithSummary("Send the SMS code that confirms account deletion (password-less accounts)")
            .Produces<ApiResponse<SendAccountDeletionCodeResult>>(StatusCodes.Status200OK)
            .Produces<ApiResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ApiResponse>(StatusCodes.Status429TooManyRequests)
            .HasApiVersion(1.0);
    }
}

public record DeleteMyAccountRequest
{
    public string? Password { get; init; }
    public string? OtpCode { get; init; }
}
