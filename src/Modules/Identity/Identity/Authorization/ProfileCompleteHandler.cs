using Microsoft.AspNetCore.Authorization;

namespace Identity.Authorization;

public class ProfileCompleteRequirement : IAuthorizationRequirement
{
}

public class ProfileCompleteHandler(ILogger<ProfileCompleteHandler> logger)
    : AuthorizationHandler<ProfileCompleteRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ProfileCompleteRequirement requirement)
    {
        var profileCompleteClaim = context.User.FindFirst("profile_complete");

        if (profileCompleteClaim == null)
        {
            logger.LogWarning("User does not have profile_complete claim");
            context.Fail();
            return Task.CompletedTask;
        }

        if (bool.TryParse(profileCompleteClaim.Value, out var isComplete) && isComplete)
        {
            context.Succeed(requirement);
        }
        else
        {
            logger.LogWarning("User profile is not complete");
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
