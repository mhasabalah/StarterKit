using Carter;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Extensions;
using Xunit;

namespace Shared.Core.Tests.Web;

/// <summary>
/// A module without endpoints must register nothing: an empty module list makes Carter scan every loaded assembly and
/// register every other module's endpoints again ("Duplicate endpoint name" at startup).
/// </summary>
public class CarterRegistrationTests
{
    [Fact]
    public void An_assembly_without_carter_modules_registers_nothing()
    {
        var services = new ServiceCollection();

        services.AddCarterWithAssemblies(typeof(string).Assembly);

        services.Should().BeEmpty();
    }

    [Fact]
    public void An_assembly_with_carter_modules_registers_exactly_its_modules()
    {
        var services = new ServiceCollection();

        services.AddCarterWithAssemblies(typeof(CarterRegistrationTests).Assembly);

        services.Where(d => d.ServiceType == typeof(ICarterModule))
            .Select(d => d.ImplementationType)
            .Should().Equal(typeof(ProbeModule));
    }

    public sealed class ProbeModule : ICarterModule
    {
        public void AddRoutes(IEndpointRouteBuilder app)
        {
        }
    }
}
