using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Shared.Core.MultiTenancy;
using Shared.Core.Web;

namespace Shared.Core.Extensions;

/// <summary>Well-known rate limit policy names. Use with <c>.RequireRateLimiting(RateLimitPolicyNames.X)</c>.</summary>
public static class RateLimitPolicyNames
{
    /// <summary>File uploads. Partitioned per user (or per IP when anonymous).</summary>
    public const string Upload = "upload";

    /// <summary>State-changing requests. Partitioned per user (or per IP when anonymous).</summary>
    public const string Create = "create";

    /// <summary>Reads / searches. Partitioned per user (or per IP when anonymous).</summary>
    public const string Search = "search";

    /// <summary>Anonymous auth endpoints (login, register, refresh, password reset). Partitioned per IP.</summary>
    public const string Auth = "auth";

    /// <summary>
    /// Sending one-time codes (SMS / e-mail / WhatsApp). Limited per IP AND, when an identifier is available
    /// (authenticated user id, or a <c>userId</c>/<c>phoneNumber</c>/<c>phone</c>/<c>email</c> route value), per identifier.
    /// Identifiers that only exist in the request body must additionally be throttled in the handler.
    /// </summary>
    public const string Otp = "otp";
}

/// <summary>How requests are grouped into independent buckets.</summary>
public enum RateLimitPartitionBy
{
    /// <summary>Authenticated user id (NameIdentifier/sub claim); anonymous callers fall back to client IP.</summary>
    UserOrIp = 0,

    /// <summary>Client IP only (IPv6 grouped by /64).</summary>
    Ip = 1,

    /// <summary>Two limits apply: one per client IP and one per user/identifier (when available).</summary>
    IpAndUser = 2,

    /// <summary>Current tenant (resolved by the tenant middleware); falls back to user/IP when there is none.</summary>
    Tenant = 3
}

/// <summary>One entry of <c>RateLimiting:Policies[]</c>.</summary>
public sealed class RateLimitPolicyOptions
{
    public RateLimitPolicyOptions()
    {
    }

    public RateLimitPolicyOptions(string name, int permitLimit, int windowSeconds, int queueLimit = 0,
        RateLimitPartitionBy partitionBy = RateLimitPartitionBy.UserOrIp)
    {
        Name = name;
        PermitLimit = permitLimit;
        WindowSeconds = windowSeconds;
        QueueLimit = queueLimit;
        PartitionBy = partitionBy;
    }

    public string Name { get; set; } = string.Empty;
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public int QueueLimit { get; set; }
    public RateLimitPartitionBy PartitionBy { get; set; } = RateLimitPartitionBy.UserOrIp;
}

/// <summary>
/// Configuration-driven, per-client rate limiting.
/// <list type="bullet">
/// <item>Every policy is a <see cref="PartitionedRateLimiter"/>: each user / IP gets its own fixed window, so one
/// client can never exhaust the budget of everyone else.</item>
/// <item>Policies come from <c>RateLimiting:Policies[]</c>; built-in defaults (<see cref="DefaultPolicies"/>) fill in any
/// missing name, so <c>auth</c> and <c>otp</c> always exist.</item>
/// <item>Rejections return 429 + <c>Retry-After</c> + a ProblemDetails body.</item>
/// </list>
/// The client IP is <c>Connection.RemoteIpAddress</c>, so <c>UseTrustedForwardedHeaders</c> must run first, and
/// <c>UseRateLimiter()</c> must run AFTER authentication for user partitioning to see the user.
/// </summary>
public static class RateLimitingExtensions
{
    private const string SectionName = "RateLimiting";

    /// <summary>Name used in logs for the per-tenant global budget (<c>RateLimiting:Tenant</c>).</summary>
    public const string TenantPolicyName = "tenant";

    /// <summary>Defaults used for any policy name not present in configuration.</summary>
    public static IReadOnlyList<RateLimitPolicyOptions> DefaultPolicies { get; } =
    [
        new(RateLimitPolicyNames.Upload, 5, 60),
        new(RateLimitPolicyNames.Create, 10, 60),
        new(RateLimitPolicyNames.Search, 60, 60),
        new(RateLimitPolicyNames.Auth, 10, 60, partitionBy: RateLimitPartitionBy.Ip),
        new(RateLimitPolicyNames.Otp, 3, 600, partitionBy: RateLimitPartitionBy.IpAndUser)
    ];

    private static readonly string[] IdentifierRouteKeys = ["userId", "phoneNumber", "phone", "email"];

    public static IServiceCollection AddConfigurableRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var policies = ResolvePolicies(configuration);

        services.AddRateLimiter(options =>
        {
            foreach (var policy in policies.Values)
            {
                var p = policy;
                options.AddPolicy(p.Name, ctx =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        p.PartitionBy switch
                        {
                            RateLimitPartitionBy.UserOrIp => GetUserOrIpKey(ctx),
                            RateLimitPartitionBy.Tenant => GetTenantKey(ctx) ?? GetUserOrIpKey(ctx),
                            _ => GetIpKey(ctx)
                        },
                        _ => CreateWindow(p)));
            }

            var globalLimiters = new List<PartitionedRateLimiter<HttpContext>>();

            // Second dimension for IpAndUser policies (e.g. "otp"): the global limiter runs in addition to the
            // endpoint policy, so a request must pass both the per-IP and the per-identifier bucket.
            var dualPolicies = policies.Values
                .Where(p => p.PartitionBy == RateLimitPartitionBy.IpAndUser)
                .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

            if (dualPolicies.Count > 0)
            {
                globalLimiters.Add(PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                {
                    var policyName = GetEndpointPolicyName(ctx);
                    if (policyName is null || !dualPolicies.TryGetValue(policyName, out var p))
                        return RateLimitPartition.GetNoLimiter(string.Empty);

                    var identifier = GetIdentifier(ctx);
                    if (identifier is null)
                        return RateLimitPartition.GetNoLimiter(string.Empty);

                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"{p.Name}|{identifier}", _ => CreateWindow(p));
                }));
            }

            // Noisy-neighbour protection: one budget per tenant across ALL its users (RateLimiting:Tenant).
            var tenantPolicy = configuration.GetSection($"{SectionName}:Tenant").Get<RateLimitPolicyOptions>();
            if (tenantPolicy is { PermitLimit: > 0, WindowSeconds: > 0 })
            {
                tenantPolicy.Name = TenantPolicyName;
                globalLimiters.Add(PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                    GetTenantKey(ctx) is { } tenantKey
                        ? RateLimitPartition.GetFixedWindowLimiter(tenantKey, _ => CreateWindow(tenantPolicy))
                        : RateLimitPartition.GetNoLimiter(string.Empty)));
            }

            if (globalLimiters.Count > 0)
            {
                options.GlobalLimiter = globalLimiters.Count == 1
                    ? globalLimiters[0]
                    : PartitionedRateLimiter.CreateChained(globalLimiters.ToArray());
            }

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, ct) => WriteRejectionAsync(context, policies, ct);
        });

        return services;
    }

    /// <summary>Configured policies merged over <see cref="DefaultPolicies"/> (configuration wins per name).</summary>
    public static IReadOnlyDictionary<string, RateLimitPolicyOptions> ResolvePolicies(IConfiguration configuration)
    {
        var configured = configuration.GetSection($"{SectionName}:Policies").Get<List<RateLimitPolicyOptions>>() ?? [];

        var result = new Dictionary<string, RateLimitPolicyOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in DefaultPolicies) result[policy.Name] = policy;

        foreach (var policy in configured)
        {
            if (string.IsNullOrWhiteSpace(policy.Name))
                throw new InvalidOperationException($"{SectionName}:Policies contains an entry without a Name.");
            if (policy.PermitLimit < 1 || policy.WindowSeconds < 1 || policy.QueueLimit < 0)
                throw new InvalidOperationException(
                    $"{SectionName}:Policies '{policy.Name}' must have PermitLimit >= 1, WindowSeconds >= 1 and QueueLimit >= 0.");
            result[policy.Name] = policy;
        }

        return result;
    }

    private static FixedWindowRateLimiterOptions CreateWindow(RateLimitPolicyOptions p) => new()
    {
        PermitLimit = p.PermitLimit,
        Window = TimeSpan.FromSeconds(p.WindowSeconds),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = p.QueueLimit,
        AutoReplenishment = true
    };

    private static string? GetEndpointPolicyName(HttpContext ctx)
    {
        var endpoint = ctx.GetEndpoint();
        if (endpoint is null || endpoint.Metadata.GetMetadata<DisableRateLimitingAttribute>() is not null)
            return null;
        return endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
    }

    private static string? GetUserId(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true) return null;
        var id = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.User.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    /// <summary>"user:{id}" for authenticated callers, otherwise "ip:{client ip}".</summary>
    public static string GetUserOrIpKey(HttpContext ctx)
    {
        var userId = GetUserId(ctx);
        return userId is not null ? $"user:{userId}" : GetIpKey(ctx);
    }

    /// <summary>"tenant:{id}" when the tenant middleware resolved a tenant for this request, else <c>null</c>.</summary>
    public static string? GetTenantKey(HttpContext ctx) =>
        ctx.Items.TryGetValue(typeof(TenantInfo), out var value) && value is TenantInfo tenant
            ? $"tenant:{tenant.Id:N}"
            : null;

    /// <summary>"ip:{client ip}" (IPv6 addresses are grouped by their /64 prefix).</summary>
    public static string GetIpKey(HttpContext ctx)
    {
        var ip = ClientIp.GetRemoteIp(ctx);
        if (ip is null) return "ip:unknown";

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            ip = new IPAddress(bytes);
        }

        return $"ip:{ip}";
    }

    private static string? GetIdentifier(HttpContext ctx)
    {
        var userId = GetUserId(ctx);
        if (userId is not null) return $"user:{userId}";

        foreach (var key in IdentifierRouteKeys)
        {
            if (ctx.Request.RouteValues.TryGetValue(key, out var value) &&
                Convert.ToString(value, CultureInfo.InvariantCulture) is { Length: > 0 } s)
                return $"{key}:{s.Trim().ToLowerInvariant()}";
        }

        return null;
    }

    private static async ValueTask WriteRejectionAsync(
        OnRejectedContext context,
        IReadOnlyDictionary<string, RateLimitPolicyOptions> policies,
        CancellationToken ct)
    {
        var http = context.HttpContext;

        int retryAfterSeconds;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        }
        else
        {
            var policyName = GetEndpointPolicyName(http);
            retryAfterSeconds = policyName is not null && policies.TryGetValue(policyName, out var p)
                ? p.WindowSeconds
                : 60;
        }

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        http.RequestServices.GetService<ILoggerFactory>()?
            .CreateLogger("RateLimiting")
            .LogWarning("Rate limit exceeded for {Path} (policy {Policy}, partition {Partition})",
                http.Request.Path.Value, GetEndpointPolicyName(http), GetUserOrIpKey(http));

        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc6585#section-4",
            Title = "Too Many Requests",
            Status = StatusCodes.Status429TooManyRequests,
            Detail = "Too many requests. Please retry later.",
            Instance = http.Request.Path
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;
        problem.Extensions["retryAfterSeconds"] = retryAfterSeconds;

        await http.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null,
            "application/problem+json", ct);
    }
}
