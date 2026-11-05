using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Shared.Core.Extensions;
using Shared.Core.Tests.Infrastructure;

namespace Shared.Core.Tests.Web;

public sealed class RateLimitingTests
{
    private static Dictionary<string, string?> Policy(string name, int permits, int windowSeconds, string? partitionBy = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["RateLimiting:Policies:0:Name"] = name,
            ["RateLimiting:Policies:0:PermitLimit"] = permits.ToString(),
            ["RateLimiting:Policies:0:WindowSeconds"] = windowSeconds.ToString(),
            ["RateLimiting:Policies:0:QueueLimit"] = "0"
        };
        if (partitionBy is not null)
            config["RateLimiting:Policies:0:PartitionBy"] = partitionBy;
        return config;
    }

    private static Task<TestApp> StartAsync(IDictionary<string, string?> config) =>
        TestApp.StartAsync(
            b => b.Services.AddConfigurableRateLimiting(b.Configuration),
            app =>
            {
                app.UseRouting();
                TestApp.UseHeaderUser(app); // stands in for UseAuthentication (runs before the limiter)
                app.UseRateLimiter();
                foreach (var name in new[] { "create", "search", "upload", "auth", "otp" })
                    app.MapGet($"/{name}", () => Results.Ok("ok")).RequireRateLimiting(name);
                app.MapGet("/otp/{phoneNumber}", (string phoneNumber) => Results.Ok(phoneNumber)).RequireRateLimiting("otp");
            },
            config);

    [Fact]
    public async Task Two_ips_do_not_share_a_bucket()
    {
        await using var app = await StartAsync(Policy("create", 2, 60));

        (await app.SendAsync("/create", "203.0.113.1")).Response.StatusCode.Should().Be(200);
        (await app.SendAsync("/create", "203.0.113.1")).Response.StatusCode.Should().Be(200);
        (await app.SendAsync("/create", "203.0.113.1")).Response.StatusCode.Should().Be(429);

        // Another client is unaffected by the first one's exhaustion.
        (await app.SendAsync("/create", "203.0.113.2")).Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Rejection_returns_429_with_retry_after_and_problem_details()
    {
        await using var app = await StartAsync(Policy("create", 1, 30));

        await app.SendAsync("/create", "198.51.100.10");
        var rejected = await app.SendAsync("/create", "198.51.100.10");

        rejected.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        rejected.Response.Headers.RetryAfter.ToString().Should().MatchRegex("^[0-9]+$");
        int.Parse(rejected.Response.Headers.RetryAfter.ToString()).Should().BeInRange(1, 30);
        rejected.Response.ContentType.Should().StartWith("application/problem+json");

        using var json = JsonDocument.Parse(await rejected.ReadBodyAsync());
        json.RootElement.GetProperty("status").GetInt32().Should().Be(429);
        json.RootElement.GetProperty("title").GetString().Should().Be("Too Many Requests");
        json.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Authenticated_users_get_their_own_bucket_even_behind_the_same_ip()
    {
        await using var app = await StartAsync(Policy("search", 1, 60));
        const string sharedIp = "192.0.2.50"; // e.g. an office NAT

        (await app.SendAsync("/search", sharedIp, r => r.Headers[TestApp.TestUserHeader] = "user-a")).Response.StatusCode.Should().Be(200);
        (await app.SendAsync("/search", sharedIp, r => r.Headers[TestApp.TestUserHeader] = "user-a")).Response.StatusCode.Should().Be(429);
        (await app.SendAsync("/search", sharedIp, r => r.Headers[TestApp.TestUserHeader] = "user-b")).Response.StatusCode.Should().Be(200);
        (await app.SendAsync("/search", sharedIp)).Response.StatusCode.Should().Be(200, "anonymous callers fall back to the IP bucket");
    }

    [Fact]
    public async Task Auth_policy_is_per_ip_even_for_authenticated_callers()
    {
        await using var app = await StartAsync(Policy("auth", 1, 60, "Ip"));

        (await app.SendAsync("/auth", "203.0.113.9", r => r.Headers[TestApp.TestUserHeader] = "u1")).Response.StatusCode.Should().Be(200);
        (await app.SendAsync("/auth", "203.0.113.9", r => r.Headers[TestApp.TestUserHeader] = "u2")).Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task Otp_policy_limits_per_ip_and_per_identifier()
    {
        await using var app = await StartAsync(Policy("otp", 2, 600, "IpAndUser"));

        // Per identifier: the same phone number is throttled even when the attacker rotates IPs.
        (await app.SendAsync("/otp/+201000000001", "203.0.113.1")).Response.StatusCode.Should().Be(200);
        (await app.SendAsync("/otp/+201000000001", "203.0.113.2")).Response.StatusCode.Should().Be(200);
        (await app.SendAsync("/otp/+201000000001", "203.0.113.3")).Response.StatusCode.Should().Be(429);

        // Per IP: one IP can't spray many identifiers.
        (await app.SendAsync("/otp/+201000000002", "198.51.100.1")).Response.StatusCode.Should().Be(200);
        (await app.SendAsync("/otp/+201000000003", "198.51.100.1")).Response.StatusCode.Should().Be(200);
        (await app.SendAsync("/otp/+201000000004", "198.51.100.1")).Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task Built_in_policies_exist_without_any_configuration()
    {
        await using var app = await StartAsync(new Dictionary<string, string?>());

        foreach (var name in new[] { "create", "search", "upload", "auth", "otp" })
            (await app.SendAsync($"/{name}", "203.0.113.77")).Response.StatusCode.Should().Be(200, name);
    }

    [Fact]
    public void Configured_policies_override_defaults_by_name_and_keep_the_rest()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(Policy("AUTH", 42, 7)).Build();

        var policies = RateLimitingExtensions.ResolvePolicies(config);

        policies.Keys.Should().Contain(["upload", "create", "search", "auth", "otp"]);
        policies["auth"].PermitLimit.Should().Be(42);
        policies["auth"].WindowSeconds.Should().Be(7);
        policies["otp"].PartitionBy.Should().Be(RateLimitPartitionBy.IpAndUser);
        policies["otp"].PermitLimit.Should().Be(3);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(5, 0)]
    public void Invalid_policy_configuration_fails_fast(int permits, int window)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(Policy("create", permits, window)).Build();

        var act = () => RateLimitingExtensions.ResolvePolicies(config);

        act.Should().Throw<InvalidOperationException>().WithMessage("*create*");
    }

    [Fact]
    public void Ipv6_clients_are_grouped_by_their_64_prefix()
    {
        var a = new DefaultHttpContext();
        a.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("2001:db8:1:2:aaaa::1");
        var b = new DefaultHttpContext();
        b.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("2001:db8:1:2:bbbb::2");
        var c = new DefaultHttpContext();
        c.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("2001:db8:1:3::1");

        RateLimitingExtensions.GetIpKey(a).Should().Be(RateLimitingExtensions.GetIpKey(b));
        RateLimitingExtensions.GetIpKey(a).Should().NotBe(RateLimitingExtensions.GetIpKey(c));
    }
}
