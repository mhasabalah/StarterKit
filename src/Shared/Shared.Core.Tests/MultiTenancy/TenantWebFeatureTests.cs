using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Web;
using MsOptions = Microsoft.Extensions.Options.Options;
using static Shared.Core.Tests.MultiTenancy.TenantTestData;

namespace Shared.Core.Tests.MultiTenancy;

/// <summary>Impersonation guard: what an impersonation token (claim <c>act</c>) may and may not call.</summary>
public sealed class ImpersonationGuardTests
{
    private static readonly Assembly ReadOnlyAssembly = typeof(ImpersonationGuardTests).Assembly;

    private static async Task<bool> PassesAsync(string method, bool impersonated, params object[] metadata)
    {
        var options = new ImpersonationGuardOptions();
        options.ReadOnlyAssemblies.Add(ReadOnlyAssembly);

        var reached = false;
        var middleware = new ImpersonationGuardMiddleware(_ =>
        {
            reached = true;
            return Task.CompletedTask;
        }, MsOptions.Create(options), NullLogger<ImpersonationGuardMiddleware>.Instance);

        var http = new DefaultHttpContext();
        http.Request.Method = method;
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (impersonated)
            claims.Add(new Claim(TenantClaimTypes.Actor, Guid.NewGuid().ToString()));
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "test"));

        try
        {
            await middleware.InvokeAsync(http);
        }
        catch (TenantAccessDeniedException ex) when (ex.Error == TenantErrors.ImpersonationRestricted)
        {
            return false;
        }

        return reached;
    }

    private static MethodInfo HandlerIn(Type type) => type.GetMethods().First();

    [Fact]
    public async Task Regular_tokens_are_never_restricted()
    {
        (await PassesAsync("POST", impersonated: false, new DisallowImpersonationMetadata())).Should().BeTrue();
    }

    [Fact]
    public async Task Endpoints_marked_DisallowImpersonation_are_blocked_for_every_method()
    {
        (await PassesAsync("GET", impersonated: true, new DisallowImpersonationMetadata())).Should().BeFalse();
        (await PassesAsync("POST", impersonated: true, new DisallowImpersonationMetadata())).Should().BeFalse();
    }

    [Fact]
    public async Task Host_endpoints_are_read_only_for_impersonation()
    {
        var host = new TenantRequirementMetadata(TenantRequirement.Host);
        (await PassesAsync("GET", impersonated: true, host)).Should().BeTrue();
        (await PassesAsync("POST", impersonated: true, host)).Should().BeFalse("switch-tenant/create-tenant would mint sessions");
    }

    [Fact]
    public async Task Read_only_assemblies_allow_reads_and_block_writes()
    {
        var handler = HandlerIn(typeof(ImpersonationGuardTests));
        (await PassesAsync("GET", impersonated: true, handler)).Should().BeTrue();
        (await PassesAsync("PUT", impersonated: true, handler)).Should().BeFalse();
    }

    [Fact]
    public async Task Ordinary_tenant_endpoints_work_while_impersonating()
    {
        var handler = HandlerIn(typeof(string)); // another assembly
        (await PassesAsync("POST", impersonated: true, handler, new TenantRequirementMetadata(TenantRequirement.Required)))
            .Should().BeTrue();
    }
}

/// <summary>Tenant-aware CORS: tenant subdomains and verified custom domains get the configured policy.</summary>
public sealed class TenantCorsPolicyProviderTests
{
    private const string Policy = "p";
    private static readonly TenantInfo Deleted = new(Guid.NewGuid(), "gone", "Gone", TenantStatus.Deleted);

    private static async Task<bool> AllowsAsync(string origin, bool development = false, bool enabled = true,
        TenantCorsOptions? cors = null)
    {
        var corsOptions = new CorsOptions();
        corsOptions.AddPolicy(Policy, p => p.WithOrigins("https://app.example.com").AllowCredentials());

        var store = new Mock<ITenantStore>();
        store.Setup(s => s.FindByIdentifierAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
                id == TenantA.Identifier ? TenantA : id == Deleted.Identifier ? Deleted : null);
        store.Setup(s => s.FindByHostAsync("shop.acme.com", It.IsAny<CancellationToken>())).ReturnsAsync(TenantA);

        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(development ? Environments.Development : Environments.Production);

        var provider = new TenantCorsPolicyProvider(MsOptions.Create(corsOptions),
            MsOptions.Create(new MultiTenancyOptions
            {
                Enabled = enabled,
                Cors = cors ?? new TenantCorsOptions { FrontendRootDomain = "app.example.com" }
            }),
            store.Object, environment.Object);

        var http = new DefaultHttpContext();
        http.Request.Headers.Origin = origin;
        var policy = await provider.GetPolicyAsync(http, Policy);
        return policy!.IsOriginAllowed(origin);
    }

    [Theory]
    [InlineData("https://app.example.com", true)] // configured origin
    [InlineData("https://acme.app.example.com", true)] // tenant subdomain
    [InlineData("https://shop.acme.com", true)] // verified custom domain
    [InlineData("https://unknown.app.example.com", false)]
    [InlineData("https://gone.app.example.com", false)] // deleted tenant
    [InlineData("https://a.acme.app.example.com", false)] // nested label
    [InlineData("http://acme.app.example.com", false)] // plain http outside Development
    [InlineData("https://evil.com", false)]
    public async Task Origins(string origin, bool allowed)
    {
        (await AllowsAsync(origin)).Should().Be(allowed);
    }

    [Fact]
    public async Task Plain_http_tenant_origins_are_allowed_in_development_only()
    {
        (await AllowsAsync("http://acme.app.example.com", development: true)).Should().BeTrue();
    }

    [Fact]
    public async Task Single_tenant_mode_uses_the_configured_policy_only()
    {
        (await AllowsAsync("https://acme.app.example.com", enabled: false)).Should().BeFalse();
    }

    [Fact]
    public async Task Custom_domains_can_be_turned_off()
    {
        (await AllowsAsync("https://shop.acme.com",
            cors: new TenantCorsOptions { FrontendRootDomain = "app.example.com", AllowVerifiedCustomDomains = false }))
            .Should().BeFalse();
    }
}
