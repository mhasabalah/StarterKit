using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.Exceptions;

namespace Shared.Core.Entitlements;

/// <summary>
/// What the current subscriber's plan allows. The subscriber is whoever pays for the request (the tenant in
/// multi-tenant mode, the signed-in user otherwise); the Subscriptions module provides the real implementation.
/// </summary>
public interface IEntitlementService
{
    /// <summary>True when the plan includes the feature <paramref name="key"/>.</summary>
    Task<bool> HasFeatureAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Numeric plan limit for <paramref name="key"/> (see <see cref="EntitlementKeys"/>), or <c>null</c> when the plan
    /// sets none (unlimited / not applicable).
    /// </summary>
    Task<int?> GetLimitAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>Well-known limit keys for <see cref="IEntitlementService.GetLimitAsync"/>.</summary>
public static class EntitlementKeys
{
    public const string MaxItems = "max_items";
    public const string MaxStorageGb = "max_storage_gb";
    public const string MaxUsers = "max_users";

    /// <summary>Plan feature: enterprise SSO (SAML/OIDC) and SCIM provisioning.</summary>
    public const string Sso = "sso";

    /// <summary>Plan feature: bulk CSV/Excel import (DataTransfer).</summary>
    public const string BulkImport = "bulk_import";

    /// <summary>Plan feature: bulk CSV/Excel export (DataTransfer).</summary>
    public const string BulkExport = "bulk_export";

    /// <summary>
    /// Metered plan feature (<c>"limit/Period"</c>, e.g. <c>"100000/Monthly"</c>): rows imported (commit) and exported.
    /// Counted per batch by the DataTransfer jobs; a job stops with <c>quota_exceeded</c> when it runs out.
    /// </summary>
    public const string DataTransferRows = "data_transfer_rows";

    /// <summary>Plan feature: AI agents (Agentic module).</summary>
    public const string Agents = "agents";

    /// <summary>Metered plan feature (<c>"limit/Period"</c>): agent runs started. One unit per run.</summary>
    public const string AgentRuns = "agent_runs";

    /// <summary>
    /// Metered plan feature (<c>"limit/Period"</c>): model tokens (input + output) of agent runs, counted per saved
    /// model turn. A run stops with <c>quota_exceeded</c> when the plan runs out.
    /// </summary>
    public const string AgentTokens = "agent_tokens";

    /// <summary>Plan limit: agent runs of one tenant that may be queued, running or waiting at the same time.</summary>
    public const string MaxConcurrentAgentRuns = "max_concurrent_agent_runs";
}

/// <summary>Default when no billing module is installed: everything is allowed, nothing is limited.</summary>
public sealed class PermissiveEntitlementService : IEntitlementService
{
    public Task<bool> HasFeatureAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<int?> GetLimitAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<int?>(null);
}

public static class EntitlementExtensions
{
    public const string FeatureNotInPlanCode = "feature_not_in_plan";

    /// <summary>
    /// Registers the permissive default. A billing module registers its own scoped implementation (AddScoped replaces
    /// this one regardless of call order).
    /// </summary>
    public static IServiceCollection AddEntitlements(this IServiceCollection services)
    {
        services.TryAddScoped<IEntitlementService, PermissiveEntitlementService>();
        services.AddQuotaDefaults();
        return services;
    }

    /// <summary>
    /// Endpoint is available only when the current plan includes feature <paramref name="key"/>; otherwise 403 with
    /// <c>errorCode = feature_not_in_plan</c>. Does not replace authentication or permission checks.
    /// </summary>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string key) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var entitlements = context.HttpContext.RequestServices.GetRequiredService<IEntitlementService>();
            if (!await entitlements.HasFeatureAsync(key, context.HttpContext.RequestAborted))
            {
                throw new ForbiddenException($"Your current plan does not include the '{key}' feature.")
                {
                    ErrorCode = FeatureNotInPlanCode
                };
            }

            return await next(context);
        });
}
