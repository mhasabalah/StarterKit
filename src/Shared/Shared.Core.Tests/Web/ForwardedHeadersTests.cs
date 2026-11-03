using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Tests.Infrastructure;
using Shared.Core.Web;

namespace Shared.Core.Tests.Web;

public sealed class ForwardedHeadersTests
{
    private static Task<TestApp> StartAsync(IDictionary<string, string?> config) =>
        TestApp.StartAsync(
            b => b.Services.AddHttpContextAccessor().AddScoped<IHttpContextService, HttpContextService>(),
            app =>
            {
                app.UseTrustedForwardedHeaders(app.Configuration);
                app.MapGet("/ip", (IHttpContextService svc) => Results.Text(svc.GetIpAddress() ?? "none"));
            },
            config);

    [Fact]
    public async Task X_forwarded_for_is_applied_when_the_peer_is_a_known_proxy()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5"
        });

        var ctx = await app.SendAsync("/ip", "10.0.0.5", r => r.Headers["X-Forwarded-For"] = "203.0.113.7");

        (await ctx.ReadBodyAsync()).Should().Be("203.0.113.7");
    }

    [Fact]
    public async Task X_forwarded_for_from_an_untrusted_peer_is_ignored()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5"
        });

        var ctx = await app.SendAsync("/ip", "198.51.100.9", r => r.Headers["X-Forwarded-For"] = "1.2.3.4");

        (await ctx.ReadBodyAsync()).Should().Be("198.51.100.9");
    }

    [Fact]
    public async Task Private_networks_are_trusted_only_when_configured()
    {
        await using (var notTrusted = await StartAsync(new Dictionary<string, string?>()))
        {
            var ctx = await notTrusted.SendAsync("/ip", "172.18.0.3", r => r.Headers["X-Forwarded-For"] = "203.0.113.8");
            (await ctx.ReadBodyAsync()).Should().Be("172.18.0.3");
        }

        await using var trusted = await StartAsync(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:TrustPrivateNetworks"] = "true"
        });
        var ctx2 = await trusted.SendAsync("/ip", "172.18.0.3", r => r.Headers["X-Forwarded-For"] = "203.0.113.8");
        (await ctx2.ReadBodyAsync()).Should().Be("203.0.113.8");
    }

    [Fact]
    public async Task Only_the_last_hop_is_trusted_so_a_client_cannot_prepend_a_fake_ip()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.5"
        });

        // Nginx appends the real peer: "<client-supplied>, <real client>".
        var ctx = await app.SendAsync("/ip", "10.0.0.5", r => r.Headers["X-Forwarded-For"] = "6.6.6.6, 203.0.113.20");

        (await ctx.ReadBodyAsync()).Should().Be("203.0.113.20");
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownProxies:0", "not-an-ip")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/99")]
    public void Invalid_proxy_configuration_fails_fast(string key, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection([new(key, value)]).Build();

        var act = () => TrustedForwardedHeadersExtensions.BuildForwardedHeadersOptions(config);

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{value}*");
    }

    [Fact]
    public void Trust_all_host_flag_is_switched_off()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("FORWARDEDHEADERS_ENABLED", "true")])
            .Build();

        TrustedForwardedHeadersExtensions.DisableTrustAllForwardedHeadersFlag(config).Should().BeTrue();
        config["ForwardedHeaders_Enabled"].Should().Be("false");
    }

    [Fact]
    public void HttpContextService_never_reads_raw_forwarding_headers()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("198.51.100.4");
        http.Request.Headers["X-Forwarded-For"] = "1.1.1.1";
        http.Request.Headers["X-Real-IP"] = "2.2.2.2";
        var accessor = Mock.Of<IHttpContextAccessor>(a => a.HttpContext == http);

        new HttpContextService(accessor).GetIpAddress().Should().Be("198.51.100.4");
    }

    [Fact]
    public void Ipv4_mapped_ipv6_addresses_are_reported_as_ipv4()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("::ffff:203.0.113.5");

        ClientIp.GetRemoteIpString(http).Should().Be("203.0.113.5");
    }
}
