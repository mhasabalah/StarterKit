using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Contracts.FeatureFlags;
using Shared.Core.Exceptions;
using Shared.Core.Exceptions.Handler;
using Shared.Core.FeatureFlags;

namespace Shared.Core.Tests.FeatureFlags;

public class FeatureFlagEvaluatorTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid User = Guid.Parse("22222222-0000-0000-0000-000000000002");

    private static FeatureFlagDefinition Flag(bool enabled = false, int rollout = 0, bool kill = false,
        Dictionary<Guid, bool>? tenants = null, Dictionary<Guid, bool>? users = null) =>
        new("f", enabled, rollout, kill, tenants ?? [], users ?? []);

    [Fact]
    public void UnknownFlag_UsesTheConfiguredDefault()
    {
        FeatureFlagEvaluator.Evaluate(null, Tenant, User, configuredDefault: true).Should().BeTrue();
        FeatureFlagEvaluator.Evaluate(null, Tenant, User, configuredDefault: false).Should().BeFalse();
    }

    [Fact]
    public void KillSwitch_BeatsEveryOverrideAndRollout()
    {
        var flag = Flag(enabled: true, rollout: 100, kill: true,
            tenants: new() { [Tenant] = true }, users: new() { [User] = true });

        FeatureFlagEvaluator.Evaluate(flag, Tenant, User, configuredDefault: true).Should().BeFalse();
    }

    [Fact]
    public void UserOverride_BeatsTenantOverride_InBothDirections()
    {
        var userOn = Flag(tenants: new() { [Tenant] = false }, users: new() { [User] = true });
        var userOff = Flag(enabled: true, tenants: new() { [Tenant] = true }, users: new() { [User] = false });

        FeatureFlagEvaluator.Evaluate(userOn, Tenant, User, false).Should().BeTrue();
        FeatureFlagEvaluator.Evaluate(userOff, Tenant, User, false).Should().BeFalse();
    }

    [Fact]
    public void TenantOverride_BeatsRolloutAndTheFlagDefault()
    {
        var off = Flag(enabled: true, rollout: 100, tenants: new() { [Tenant] = false });
        var on = Flag(enabled: false, rollout: 0, tenants: new() { [Tenant] = true });

        FeatureFlagEvaluator.Evaluate(off, Tenant, null, true).Should().BeFalse();
        FeatureFlagEvaluator.Evaluate(on, Tenant, null, false).Should().BeTrue();
    }

    [Fact]
    public void Overrides_ApplyOnlyToTheirOwnTargets()
    {
        var flag = Flag(tenants: new() { [Tenant] = true }, users: new() { [User] = true });

        FeatureFlagEvaluator.Evaluate(flag, Guid.NewGuid(), Guid.NewGuid(), true).Should().BeFalse("no override matches, flag default off");
        FeatureFlagEvaluator.Evaluate(flag, Tenant, Guid.NewGuid(), false).Should().BeTrue();
        FeatureFlagEvaluator.Evaluate(flag, null, User, false).Should().BeTrue();
    }

    [Fact]
    public void Rollout_FullAndZero_AndFlagDefault()
    {
        FeatureFlagEvaluator.Evaluate(Flag(enabled: false, rollout: 100), Tenant, User, false).Should().BeTrue();
        FeatureFlagEvaluator.Evaluate(Flag(enabled: true, rollout: 0), Tenant, User, false).Should().BeTrue("no rollout -> Enabled");
        FeatureFlagEvaluator.Evaluate(Flag(enabled: false, rollout: 0), Tenant, User, true).Should().BeFalse("a stored flag ignores the configured default");
    }

    [Fact]
    public void Bucket_IsStable_AcrossCallsAndInstances_AndKnownValuesNeverChange()
    {
        var tenant = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var first = FeatureFlagEvaluator.Bucket("new-checkout", tenant);

        for (var i = 0; i < 100; i++)
            FeatureFlagEvaluator.Bucket("new-checkout", tenant).Should().Be(first);

        // Pinned: SHA-256("new-checkout|<tenant:N>") first 4 bytes big-endian mod 100. A change here would silently
        // move tenants in or out of every running rollout.
        first.Should().Be(ExpectedBucket("new-checkout", tenant));
        first.Should().BeInRange(0, 99);
    }

    [Fact]
    public void Rollout_RaisingThePercentage_OnlyAddsTenants()
    {
        var tenants = Enumerable.Range(0, 500).Select(i => new Guid(i, 0, 0, new byte[8])).ToList();
        bool On(Guid t, int percent) => FeatureFlagEvaluator.Evaluate(Flag(rollout: percent), t, null, false);

        var at20 = tenants.Where(t => On(t, 20)).ToHashSet();
        var at60 = tenants.Where(t => On(t, 60)).ToHashSet();

        at20.IsSubsetOf(at60).Should().BeTrue();
        at20.Count.Should().BeInRange(60, 140, "about 20% of 500");
        at60.Count.Should().BeInRange(240, 360, "about 60% of 500");
    }

    [Fact]
    public void Rollout_BucketsEachFlagIndependently()
    {
        var tenants = Enumerable.Range(0, 200).Select(i => new Guid(i, 1, 0, new byte[8])).ToList();

        var differing = tenants.Count(t =>
            FeatureFlagEvaluator.Bucket("flag-a", t) != FeatureFlagEvaluator.Bucket("flag-b", t));

        differing.Should().BeGreaterThan(150);
    }

    private static int ExpectedBucket(string key, Guid tenantId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{key}|{tenantId:N}"));
        var value = (uint)hash[0] << 24 | (uint)hash[1] << 16 | (uint)hash[2] << 8 | hash[3];
        return (int)(value % 100);
    }
}

public class FeatureFlagPortTests
{
    private static ServiceProvider Provider(Dictionary<string, string?> config, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
        extra?.Invoke(services);
        services.AddFeatureFlags();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ConfigDefault_ReadsFeatureFlagsDefaults_AndUnknownKeysAreOff()
    {
        using var provider = Provider(new()
        {
            ["FeatureFlags:Defaults:beta-ui"] = "true",
            ["FeatureFlags:Defaults:old-ui"] = "false"
        });
        using var scope = provider.CreateScope();
        var flags = scope.ServiceProvider.GetRequiredService<IFeatureFlags>();

        (await flags.IsEnabledAsync("beta-ui")).Should().BeTrue();
        (await flags.IsEnabledAsync("old-ui")).Should().BeFalse();
        (await flags.IsEnabledAsync("never-heard-of-it")).Should().BeFalse();
        (await flags.GetAllAsync()).Should().BeEquivalentTo(new Dictionary<string, bool> { ["beta-ui"] = true, ["old-ui"] = false });
    }

    [Fact]
    public void AddFeatureFlags_KeepsAnExistingRegistration()
    {
        using var provider = Provider([], s => s.AddScoped<IFeatureFlags>(_ => Mock.Of<IFeatureFlags>()));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IFeatureFlags>().Should().NotBeOfType<ConfigFeatureFlags>();
    }
}

public class RequireFlagTests
{
    private static async Task<object?> RunAsync(IFeatureFlags flags, string key)
    {
        var services = new ServiceCollection().AddSingleton(flags).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };

        var conventions = new List<Action<EndpointBuilder>>();
        new ConventionCollector(conventions).RequireFlag(key);
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0);
        conventions.ForEach(c => c(endpoint));

        var factoryContext = new EndpointFilterFactoryContext
        {
            MethodInfo = typeof(RequireFlagTests).GetMethod(nameof(Handler))!,
            ApplicationServices = services
        };
        EndpointFilterDelegate pipeline = _ => ValueTask.FromResult<object?>("ran");
        foreach (var factory in endpoint.FilterFactories.AsEnumerable().Reverse())
            pipeline = factory(factoryContext, pipeline);

        return await pipeline(new DefaultEndpointFilterInvocationContext(http));
    }

    public static string Handler() => "ran";

    [Fact]
    public async Task FlagOn_RunsTheEndpoint()
    {
        var flags = new Mock<IFeatureFlags>();
        flags.Setup(f => f.IsEnabledAsync("beta", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await RunAsync(flags.Object, "beta")).Should().Be("ran");
    }

    [Fact]
    public async Task FlagOff_Returns404_AsIfTheRouteDidNotExist()
    {
        var flags = new Mock<IFeatureFlags>();
        flags.Setup(f => f.IsEnabledAsync("beta", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => RunAsync(flags.Object, "beta");

        var ex = (await act.Should().ThrowAsync<NotFoundException>()).Which;
        ExceptionStatusMapper.Map(ex).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task UnknownFlag_WithNoConfiguredDefault_Returns404()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddFeatureFlags().BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => RunAsync(scope.ServiceProvider.GetRequiredService<IFeatureFlags>(), "dark-launch");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private sealed class ConventionCollector(List<Action<EndpointBuilder>> conventions) : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention) => conventions.Add(convention);
        public void Finally(Action<EndpointBuilder> finallyConvention) { }
    }
}
