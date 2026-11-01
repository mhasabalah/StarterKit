using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Shared.Core.Authorization;

/// <summary>
/// Authorization requirement that allows anonymous access on page 1
/// and requires authentication on page 2+.
/// Register via services.AddAuthorization(o => o.AddPolicy("PageGate", p =>
///     p.AddRequirements(new PageGateRequirement())));
/// </summary>
public class PageGateRequirement : IAuthorizationRequirement { }

public class PageGateAuthorizationHandler : AuthorizationHandler<PageGateRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PageGateRequirement requirement)
    {
        // Endpoint routing sets Resource to HttpContext — no IHttpContextAccessor needed.
        // Non-HTTP callers (background jobs) get a free pass.
        if (context.Resource is not HttpContext httpCtx)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var pageNumberStr = httpCtx.Request.Query["pageNumber"].FirstOrDefault();
        var pageNumber = int.TryParse(pageNumberStr, out var p) ? p : 1;

        // Page 1 is always public; page 2+ requires an authenticated user
        if (pageNumber <= 1 || context.User.Identity?.IsAuthenticated == true)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
