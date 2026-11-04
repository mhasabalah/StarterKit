using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Entitlements;
using Shared.Core.Exceptions;
using Shared.Core.Exceptions.Handler;

namespace Shared.Core.Tests.Entitlements;

public class RequireFeatureTests
{
    /// <summary>Applies .RequireFeature to a real endpoint builder and runs the resulting filter.</summary>
    private static async Task<object?> RunAsync(IEntitlementService service, string feature)
    {
        var services = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };

        var conventions = new List<Action<EndpointBuilder>>();
        new ConventionCollector(conventions).RequireFeature(feature);
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0);
        conventions.ForEach(c => c(endpoint));

        var factoryContext = new EndpointFilterFactoryContext
        {
            MethodInfo = typeof(RequireFeatureTests).GetMethod(nameof(Handler))!,
            ApplicationServices = services
        };
        EndpointFilterDelegate pipeline = _ => ValueTask.FromResult<object?>("ran");
        foreach (var factory in endpoint.FilterFactories.AsEnumerable().Reverse())
            pipeline = factory(factoryContext, pipeline);

        return await pipeline(new DefaultEndpointFilterInvocationContext(http));
    }

    public static string Handler() => "ran";

    [Fact]
    public async Task PermissiveDefault_AllowsEveryFeature_AndReportsNoLimits()
    {
        var service = new PermissiveEntitlementService();

        (await RunAsync(service, "anything")).Should().Be("ran");
        (await service.GetLimitAsync(EntitlementKeys.MaxUsers)).Should().BeNull();
    }

    [Fact]
    public async Task FeatureInPlan_RunsEndpoint()
    {
        var service = new Mock<IEntitlementService>();
        service.Setup(x => x.HasFeatureAsync("reports", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await RunAsync(service.Object, "reports")).Should().Be("ran");
    }

    [Fact]
    public async Task FeatureNotInPlan_IsForbidden_WithFeatureNotInPlanCode()
    {
        var service = new Mock<IEntitlementService>();
        service.Setup(x => x.HasFeatureAsync("reports", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var act = () => RunAsync(service.Object, "reports");

        var ex = (await act.Should().ThrowAsync<ForbiddenException>()).Which;
        var mapping = ExceptionStatusMapper.Map(ex);
        mapping.StatusCode.Should().Be(403);
        mapping.ErrorCode.Should().Be("feature_not_in_plan");
    }

    [Fact]
    public void AddEntitlements_RegistersPermissiveDefault_ButKeepsAnExistingRegistration()
    {
        var custom = new ServiceCollection();
        custom.AddScoped<IEntitlementService, DenyAllEntitlementService>();
        custom.AddEntitlements();
        using var customProvider = custom.BuildServiceProvider();
        using var customScope = customProvider.CreateScope();
        customScope.ServiceProvider.GetRequiredService<IEntitlementService>().Should().BeOfType<DenyAllEntitlementService>();

        using var bareProvider = new ServiceCollection().AddEntitlements().BuildServiceProvider();
        using var bareScope = bareProvider.CreateScope();
        bareScope.ServiceProvider.GetRequiredService<IEntitlementService>().Should().BeOfType<PermissiveEntitlementService>();
    }

    private sealed class DenyAllEntitlementService : IEntitlementService
    {
        public Task<bool> HasFeatureAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<int?> GetLimitAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<int?>(1);
    }

    private sealed class ConventionCollector(List<Action<EndpointBuilder>> conventions) : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention) => conventions.Add(convention);
        public void Finally(Action<EndpointBuilder> finallyConvention) { }
    }
}
