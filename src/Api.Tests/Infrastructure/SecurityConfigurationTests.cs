using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Shared.Core.Extensions;
using Shared.Core.Pagination;
using Shared.Core.Web;

namespace Api.Tests.Infrastructure;

/// <summary>
/// Guards the host configuration of the shared security features (rate limiting, forwarded headers, paging)
/// against the real appsettings files and the real endpoint source.
/// </summary>
public sealed class SecurityConfigurationTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ApiDir = Path.Combine(RepoRoot, "src", "Api");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarterKit.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("StarterKit.slnx not found above the test output folder.");
    }

    private static IConfiguration Settings(string environment) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ApiDir, "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(ApiDir, $"appsettings.{environment}.json"), optional: true)
            .Build();

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Docker")]
    public void Every_policy_name_used_by_an_endpoint_is_defined(string environment)
    {
        var policies = RateLimitingExtensions.ResolvePolicies(Settings(environment));
        var constants = typeof(RateLimitPolicyNames).GetFields()
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        var literal = new Regex("""RequireRateLimiting\(\s*"(?<name>[^"]+)"\s*\)""");
        var constant = new Regex("""RequireRateLimiting\(\s*RateLimitPolicyNames\.(?<name>\w+)\s*\)""");

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                 !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in literal.Matches(text))
                used.Add(m.Groups["name"].Value);
            // Unknown constant names can't compile in real code; they only appear in doc comments (e.g. "X").
            foreach (Match m in constant.Matches(text))
                if (constants.TryGetValue(m.Groups["name"].Value, out var name))
                    used.Add(name);
        }

        used.Should().NotBeEmpty();
        used.Should().OnlyContain(name => policies.ContainsKey(name),
            "a RequireRateLimiting(name) without a registered policy throws at request time");
    }

    [Fact]
    public void Auth_and_otp_policies_are_partitioned_as_documented()
    {
        var policies = RateLimitingExtensions.ResolvePolicies(Settings("Production"));

        policies[RateLimitPolicyNames.Auth].PartitionBy.Should().Be(RateLimitPartitionBy.Ip);
        policies[RateLimitPolicyNames.Otp].PartitionBy.Should().Be(RateLimitPartitionBy.IpAndUser);
        policies[RateLimitPolicyNames.Otp].PermitLimit.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public void Production_trusts_only_loopback_proxies_by_default()
    {
        var options = TrustedForwardedHeadersExtensions.BuildForwardedHeadersOptions(Settings("Production"));

        options.KnownIPNetworks.Should().OnlyContain(n => System.Net.IPAddress.IsLoopback(n.BaseAddress));
        options.ForwardLimit.Should().Be(1);
    }

    [Fact]
    public void Docker_trusts_the_private_proxy_networks()
    {
        var options = TrustedForwardedHeadersExtensions.BuildForwardedHeadersOptions(Settings("Docker"));

        options.KnownIPNetworks.Should().Contain(n => n.Contains(System.Net.IPAddress.Parse("172.18.0.2")));
        options.KnownIPNetworks.Should().NotContain(n => n.Contains(System.Net.IPAddress.Parse("203.0.113.1")));
    }

    [Fact]
    public void Paging_limits_are_configured_and_consistent()
    {
        var paging = Settings("Production").GetSection(PagingOptions.SectionName);

        var max = paging.GetValue<int>("MaxPageSize");
        max.Should().BeInRange(1, 1000);
        paging.GetValue<int>("AnonymousMaxPageSize").Should().BeInRange(1, max);
        paging.GetValue<int>("DefaultPageSize").Should().BeInRange(1, max);
    }
}
