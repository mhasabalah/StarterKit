using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Shared.Core.Pagination;

namespace Shared.Core.Authorization;

/// <summary>
/// Authorization requirement for anonymous list/search endpoints:
/// <list type="bullet">
/// <item>Authenticated users: always allowed (page size is still clamped by the paging helpers).</item>
/// <item>Anonymous users: only page 1, and only <c>pageSize</c> between 1 and <see cref="MaxAnonymousPageSize"/>
/// (absent = default page size). A malformed <c>pageNumber</c>/<c>pageSize</c> is treated as a violation.</item>
/// </list>
/// Register via <c>services.AddAuthorization(o =&gt; o.AddPolicy("PageGate", p =&gt; p.AddRequirements(new PageGateRequirement())));</c>
/// </summary>
public class PageGateRequirement : IAuthorizationRequirement
{
    /// <summary>Uses <see cref="PagingOptions.AnonymousMaxPageSize"/> (config <c>Paging:AnonymousMaxPageSize</c>).</summary>
    public PageGateRequirement()
    {
    }

    public PageGateRequirement(int maxAnonymousPageSize)
    {
        if (maxAnonymousPageSize < 1) throw new ArgumentOutOfRangeException(nameof(maxAnonymousPageSize));
        _maxAnonymousPageSize = maxAnonymousPageSize;
    }

    private readonly int? _maxAnonymousPageSize;

    public int MaxAnonymousPageSize => _maxAnonymousPageSize ?? PagingOptions.AnonymousMaxPageSize;
}

public class PageGateAuthorizationHandler : AuthorizationHandler<PageGateRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PageGateRequirement requirement)
    {
        // Endpoint routing sets Resource to HttpContext — no IHttpContextAccessor needed.
        // Non-HTTP callers (background jobs) get a free pass.
        if (context.Resource is not HttpContext httpCtx || context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (IsAllowedAnonymously(httpCtx.Request.Query, requirement.MaxAnonymousPageSize))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }

    internal static bool IsAllowedAnonymously(IQueryCollection query, int maxAnonymousPageSize)
    {
        if (!TryReadInt(query, "pageNumber", out var pageNumber) || !TryReadInt(query, "pageSize", out var pageSize))
            return false;

        var pageOk = pageNumber is null || pageNumber <= 1;
        var sizeOk = pageSize is null || (pageSize >= 1 && pageSize <= maxAnonymousPageSize);
        return pageOk && sizeOk;
    }

    /// <summary>Absent → (true, null). Present but not a single integer → false.</summary>
    private static bool TryReadInt(IQueryCollection query, string key, out int? value)
    {
        value = null;
        if (!query.TryGetValue(key, out var raw) || raw.Count == 0) return true;
        if (raw.Count > 1 || !int.TryParse(raw[0], out var parsed)) return false;
        value = parsed;
        return true;
    }
}
