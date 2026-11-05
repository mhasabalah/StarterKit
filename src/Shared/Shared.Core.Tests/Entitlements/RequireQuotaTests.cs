using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Entitlements;
using Shared.Core.Exceptions.Handler;

namespace Shared.Core.Tests.Entitlements;

public class RequireQuotaTests
{
    private static async Task<object?> RunAsync(IQuotaService service, string key, long quantity = 1)
    {
        var services = new ServiceCollection().AddSingleton(service).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };

        var conventions = new List<Action<EndpointBuilder>>();
        new ConventionCollector(conventions).RequireQuota(key, quantity);
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0);
        conventions.ForEach(c => c(endpoint));

        var factoryContext = new EndpointFilterFactoryContext
        {
            MethodInfo = typeof(RequireQuotaTests).GetMethod(nameof(Handler))!,
            ApplicationServices = services
        };
        EndpointFilterDelegate pipeline = _ => ValueTask.FromResult<object?>("ran");
        foreach (var factory in endpoint.FilterFactories.AsEnumerable().Reverse())
            pipeline = factory(factoryContext, pipeline);

        return await pipeline(new DefaultEndpointFilterInvocationContext(http));
    }

    public static string Handler() => "ran";

    [Fact]
    public async Task PermissiveDefault_AllowsAndCountsNothing()
    {
        (await RunAsync(new PermissiveQuotaService(), "api_calls")).Should().Be("ran");
        (await new NoopUsageMeter().GetUsageAsync("api_calls")).Should().Be(0);
    }

    [Fact]
    public async Task WithinQuota_RunsEndpoint_AndConsumesTheRequestedQuantity()
    {
        var service = new Mock<IQuotaService>();
        service.Setup(x => x.TryConsumeAsync("export", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaResult(true, 5, 100));

        (await RunAsync(service.Object, "export", 5)).Should().Be("ran");

        service.Verify(x => x.TryConsumeAsync("export", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QuotaUsedUp_Is429_WithQuotaExceededCode_AndTheEndpointDoesNotRun()
    {
        var service = new Mock<IQuotaService>();
        service.Setup(x => x.TryConsumeAsync("api_calls", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QuotaResult(false, 1000, 1000));

        var act = () => RunAsync(service.Object, "api_calls");

        var ex = (await act.Should().ThrowAsync<QuotaExceededException>()).Which;
        ex.Used.Should().Be(1000);
        ex.Limit.Should().Be(1000);
        var mapping = ExceptionStatusMapper.Map(ex);
        mapping.StatusCode.Should().Be(429);
        mapping.ErrorCode.Should().Be("quota_exceeded");
    }

    [Fact]
    public void QuantityMustBePositive()
    {
        var act = () => new ConventionCollector([]).RequireQuota("x", 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class ConventionCollector(List<Action<EndpointBuilder>> conventions) : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention) => conventions.Add(convention);
        public void Finally(Action<EndpointBuilder> finallyConvention) { }
    }
}
