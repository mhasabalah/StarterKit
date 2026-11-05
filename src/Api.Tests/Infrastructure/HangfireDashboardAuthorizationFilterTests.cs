using System.Net;
using System.Security.Claims;
using Api.Infrastructure;
using Hangfire;
using Hangfire.AspNetCore;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Authorization;

namespace Api.Tests.Infrastructure;

public sealed class HangfireDashboardAuthorizationFilterTests
{
    private sealed class NoStorage : Hangfire.JobStorage
    {
        public override Hangfire.Storage.IStorageConnection GetConnection() => throw new NotSupportedException();
        public override Hangfire.Storage.IMonitoringApi GetMonitoringApi() => throw new NotSupportedException();
    }

    private static bool Authorize(ClaimsPrincipal user)
    {
        var services = new ServiceCollection()
            .AddSingleton<IOptions<HangfireDashboardAuthOptions>>(Options.Create(new HangfireDashboardAuthOptions
            {
                Enabled = true,
                AllowLocalRequestsWithoutAuthentication = false
            }))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { User = user, RequestServices = services };
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        http.Response.Body = new MemoryStream();

        return new HangfireDashboardAuthorizationFilter().Authorize(new AspNetCoreDashboardContext(new NoStorage(), new DashboardOptions(), http));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "test"));

    [Fact]
    public void Admin_user_opens_the_dashboard()
    {
        Authorize(Principal(new Claim(ClaimTypes.Role, "Admin"))).Should().BeTrue();
    }

    [Fact]
    public void Api_key_principal_never_opens_the_dashboard_even_when_it_carries_an_admin_role_claim()
    {
        Authorize(Principal(
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ApiKeyClaimTypes.KeyId, Guid.NewGuid().ToString()),
            new Claim(ApiKeyClaimTypes.Scope, Permissions.BillingRead))).Should().BeFalse();
    }
}
