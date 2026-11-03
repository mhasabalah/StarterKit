using System.Security.Claims;
using Identity.Authorization;
using Identity.Domains.Roles.Constants;
using Identity.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;
using RoleConstants = Identity.Domains.Roles.Constants.RoleConstants;

namespace Identity.Tests.Authorization;

/// <summary>Runs the REAL policies (AddIdentityAuthorizationPolicies) through IAuthorizationService.</summary>
public sealed class PermissionPolicyTests
{
    private static ServiceProvider Build(string environment = "Production", Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == environment));
        services.AddIdentityAuthorizationPolicies(configuration);
        return services.BuildServiceProvider();
    }

    /// <summary>A principal shaped like a token from JwtTokenGenerator: roles + their permissions (+ amr).</summary>
    private static ClaimsPrincipal Caller(string[] roles, bool mfa = false, string amrClaimType = AuthenticationMethods.ClaimType)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.AddRange(RolePermissionMap.For(roles).Select(p => new Claim(Permissions.ClaimType, p)));
        if (mfa) claims.Add(new Claim(amrClaimType, AuthenticationMethods.Mfa));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static async Task<AuthorizationResult> Authorize(
        ServiceProvider sp, ClaimsPrincipal user, string policy, object? resource = null) =>
        await sp.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, resource, policy);

    [Fact]
    public void One_policy_is_registered_per_permission()
    {
        using var sp = Build();
        var options = sp.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        foreach (var permission in Permissions.All)
        {
            var policy = options.GetPolicy(Permissions.PolicyName(permission));
            policy.Should().NotBeNull(permission);
            policy!.Requirements.OfType<PermissionRequirement>().Should().ContainSingle(r => r.Permission == permission);
            policy.Requirements.Should().Contain(MfaRequirement.Instance);
        }
    }

    [Theory]
    [InlineData(RoleConstants.Finance, Permissions.BillingRead, true)]
    [InlineData(RoleConstants.Finance, Permissions.BillingApprove, true)]
    [InlineData(RoleConstants.Finance, Permissions.UsersRead, false)]
    [InlineData(RoleConstants.Support, Permissions.BillingRead, true)]
    [InlineData(RoleConstants.Support, Permissions.BillingApprove, false)]
    [InlineData(RoleConstants.Support, Permissions.AuditRead, false)]
    [InlineData(RoleConstants.Admin, Permissions.AdminsManage, false)]
    [InlineData(RoleConstants.Admin, Permissions.UsersManage, true)]
    [InlineData(RoleConstants.SuperAdmin, Permissions.AdminsManage, true)]
    [InlineData(RoleConstants.User, Permissions.BillingRead, false)]
    public async Task Permission_policy_follows_the_role_map(string role, string permission, bool allowed)
    {
        using var sp = Build();

        var result = await Authorize(sp, Caller([role], mfa: true), Permissions.PolicyName(permission));

        result.Succeeded.Should().Be(allowed);
    }

    [Fact]
    public async Task Anonymous_caller_is_rejected()
    {
        using var sp = Build();

        var result = await Authorize(sp, new ClaimsPrincipal(new ClaimsIdentity()), Permissions.PolicyName(Permissions.BillingRead));

        result.Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData(RoleConstants.SuperAdmin)]
    [InlineData(RoleConstants.Admin)]
    [InlineData(RoleConstants.Finance)]
    public async Task Enforce_mode_rejects_privileged_roles_without_mfa_with_an_mfa_only_failure(string role)
    {
        using var sp = Build("Production");

        var result = await Authorize(sp, Caller([role]), Permissions.PolicyName(Permissions.BillingRead));

        result.Succeeded.Should().BeFalse();
        MfaAwareAuthorizationResultHandler.IsMfaOnlyFailure(result.Failure).Should().BeTrue();
    }

    [Theory]
    [InlineData(AuthenticationMethods.ClaimType)]
    [InlineData(AuthenticationMethods.MappedClaimType)]
    public async Task Enforce_mode_accepts_an_mfa_session_whatever_the_amr_claim_type(string claimType)
    {
        using var sp = Build("Production");

        var result = await Authorize(sp, Caller([RoleConstants.Finance], mfa: true, claimType),
            Permissions.PolicyName(Permissions.BillingApprove));

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Roles_outside_RequiredForRoles_do_not_need_mfa()
    {
        using var sp = Build("Production");

        var result = await Authorize(sp, Caller([RoleConstants.Support]), Permissions.PolicyName(Permissions.BillingRead));

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Missing_permission_is_never_reported_as_mfa_required()
    {
        using var sp = Build("Production");

        // Finance without MFA and without users.read: the permission is the real problem.
        var result = await Authorize(sp, Caller([RoleConstants.Finance]), Permissions.PolicyName(Permissions.UsersRead));

        result.Succeeded.Should().BeFalse();
        MfaAwareAuthorizationResultHandler.IsMfaOnlyFailure(result.Failure).Should().BeFalse();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task Warn_is_the_default_in_development_like_environments_and_flags_the_response(string environment)
    {
        using var sp = Build(environment);
        var http = new DefaultHttpContext();

        var result = await Authorize(sp, Caller([RoleConstants.Admin]), Permissions.PolicyName(Permissions.UsersRead), http);

        result.Succeeded.Should().BeTrue();
        http.Response.Headers[AuthorizationErrorCodes.HeaderName].ToString().Should().Be(AuthorizationErrorCodes.MfaRequired);
    }

    [Theory]
    [InlineData("Production", TwoFactorEnforcementMode.Enforce)]
    [InlineData("Staging", TwoFactorEnforcementMode.Enforce)]
    [InlineData("Development", TwoFactorEnforcementMode.Warn)]
    [InlineData("Testing", TwoFactorEnforcementMode.Warn)]
    public void Enforcement_mode_defaults_per_environment(string environment, TwoFactorEnforcementMode expected)
    {
        using var sp = Build(environment);

        var options = sp.GetRequiredService<IOptionsMonitor<TwoFactorOptions>>().CurrentValue;

        options.EffectiveEnforcementMode.Should().Be(expected);
        options.EffectiveRequiredForRoles.Should()
            .BeEquivalentTo([RoleConstants.SuperAdmin, RoleConstants.Admin, RoleConstants.Finance]);
    }

    [Fact]
    public async Task Configured_mode_and_roles_override_the_defaults()
    {
        using var sp = Build("Development", new()
        {
            ["Identity:TwoFactor:EnforcementMode"] = "Enforce",
            ["Identity:TwoFactor:RequiredForRoles:0"] = RoleConstants.Support
        });

        var support = await Authorize(sp, Caller([RoleConstants.Support]), Permissions.PolicyName(Permissions.BillingRead));
        var finance = await Authorize(sp, Caller([RoleConstants.Finance]), Permissions.PolicyName(Permissions.BillingRead));

        support.Succeeded.Should().BeFalse("Support is now in RequiredForRoles and the mode is Enforce");
        finance.Succeeded.Should().BeTrue("Finance is no longer in RequiredForRoles");
    }

    [Theory]
    [InlineData(PolicyConstants.AdminOnly, RoleConstants.Admin)]
    [InlineData(PolicyConstants.SuperAdminOnly, RoleConstants.SuperAdmin)]
    public async Task Role_based_admin_policies_still_work_and_also_require_mfa(string policy, string role)
    {
        using var sp = Build("Production");

        (await Authorize(sp, Caller([role], mfa: true), policy)).Succeeded.Should().BeTrue();
        (await Authorize(sp, Caller([RoleConstants.User], mfa: true), policy)).Succeeded.Should().BeFalse();

        var withoutMfa = await Authorize(sp, Caller([role]), policy);
        withoutMfa.Succeeded.Should().BeFalse();
        MfaAwareAuthorizationResultHandler.IsMfaOnlyFailure(withoutMfa.Failure).Should().BeTrue();
    }

    [Fact]
    public async Task Mfa_only_403_has_the_mfa_required_error_code_and_header()
    {
        using var sp = Build("Production");
        var policy = sp.GetRequiredService<IOptions<AuthorizationOptions>>().Value
            .GetPolicy(Permissions.PolicyName(Permissions.BillingRead))!;
        var result = await Authorize(sp, Caller([RoleConstants.Finance]), Permissions.PolicyName(Permissions.BillingRead));
        var http = new DefaultHttpContext { Response = { Body = new MemoryStream() }, RequestServices = sp };

        await new MfaAwareAuthorizationResultHandler().HandleAsync(
            _ => Task.CompletedTask, http, policy, PolicyAuthorizationResult.Forbid(result.Failure));

        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        http.Response.Headers[AuthorizationErrorCodes.HeaderName].ToString().Should().Be(AuthorizationErrorCodes.MfaRequired);
        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        body.Should().Contain("\"mfa_required\"");
    }
}
