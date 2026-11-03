using Identity.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Shared.Core.Authorization;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;

namespace Identity.Authorization;

/// <summary>
/// Every authorization policy of the app (registered by the Identity module, used by all modules) plus the
/// <c>Identity:TwoFactor</c> options they depend on. Public so tests can build the exact production policies.
/// </summary>
public static class IdentityAuthorizationExtensions
{
    public static IServiceCollection AddIdentityAuthorizationPolicies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<TwoFactorOptions>()
            .Bind(configuration.GetSection(TwoFactorOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                // Not configured: enforce everywhere except local development and the E2E/Testing host.
                options.EnforcementMode ??= environment.IsDevelopmentLike()
                    ? TwoFactorEnforcementMode.Warn
                    : TwoFactorEnforcementMode.Enforce;
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(PolicyConstants.UserOnly, policy =>
                policy.RequireAuthenticatedUser());

            // Admin policies also carry the MFA rule (Identity:TwoFactor), so it cannot be bypassed on endpoints
            // that are still role-based.
            options.AddPolicy(PolicyConstants.AdminOnly, policy => policy
                .RequireRole(RoleConstants.Admin, RoleConstants.SuperAdmin)
                .AddRequirements(MfaRequirement.Instance));

            options.AddPolicy(PolicyConstants.SuperAdminOnly, policy => policy
                .RequireRole(RoleConstants.SuperAdmin)
                .AddRequirements(MfaRequirement.Instance));

            options.AddPolicy(PolicyConstants.ProfileComplete, policy =>
                policy.Requirements.Add(new ProfileCompleteRequirement()));

            // One policy per permission: "perm:<permission>" = authenticated + permission claim (derived from roles by
            // PermissionClaimsTransformation) + MFA rule.
            // Endpoints use .RequirePermission(Permissions.X). Role -> permission mapping: RolePermissionMap.
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(Permissions.PolicyName(permission), policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission), MfaRequirement.Instance));
            }
        });

        // Tokens carry roles only; permissions are derived per request (provider-neutral, see RolePermissionMap).
        services.AddRolePermissionClaims();

        services.AddScoped<IAuthorizationHandler, ProfileCompleteHandler>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, MfaAuthorizationHandler>();

        // 403s caused only by the MFA rule get an explicit "mfa_required" body + header.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, MfaAwareAuthorizationResultHandler>();

        return services;
    }
}
