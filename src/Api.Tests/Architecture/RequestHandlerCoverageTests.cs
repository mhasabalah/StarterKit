using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Mediation;

namespace Api.Tests.Architecture;

/// <summary>
/// Compile-time-like safety for the owned mediator (wiki/Decisions/0007-owned-mediator-port.md): every request type
/// (command / query) in the production assemblies has a handler in the REAL application container, and every
/// constructor dependency of that handler is registered. Without this, a missing or mis-wired
/// handler only surfaces when the request is first sent.
/// </summary>
public sealed class RequestHandlerCoverageTests
{
    private static readonly string[] Production =
        ModuleBoundaryTests.Modules
            .SelectMany(m => new[] { m, $"{m}.Contracts" })
            .Concat(ModuleBoundaryTests.Ports)
            .Concat(["Shared.Contracts", "Shared.Core", "Api"])
            .ToArray();

    private static IEnumerable<(Type Request, Type Handler)> Requests() =>
        Production
            .SelectMany(a => Assembly.Load(a).GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>))
                .Select(i => (t, typeof(IRequestHandler<,>).MakeGenericType(t, i.GetGenericArguments()[0]))));

    [Fact]
    public void Every_request_has_a_registered_handler()
    {
        var services = RealHost.Services.GetRequiredService<IServiceProviderIsService>();

        var missing = Requests()
            .Where(r => !services.IsService(r.Handler))
            .Select(r => r.Request.FullName)
            .ToList();

        Requests().Should().NotBeEmpty("the scan must find the commands and queries");
        missing.Should().BeEmpty("ISender.Send would throw at runtime for these requests");
    }

    /// <summary>
    /// Checks the wiring WITHOUT instantiating anything (construction would open Hangfire/Postgres connections that
    /// these database-free tests deliberately lack): every constructor dependency of every handler is registered.
    /// </summary>
    [Fact]
    public void Every_handler_dependency_is_registered()
    {
        var services = RealHost.Services.GetRequiredService<IServiceProviderIsService>();
        var implementations = Production
            .SelectMany(a => Assembly.Load(a).GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(t => t.GetInterfaces().Select(i => (Contract: i, Implementation: t)))
            .ToLookup(x => x.Contract, x => x.Implementation);

        var handlers = Requests().SelectMany(r => implementations[r.Handler]).Distinct().ToList();
        handlers.Should().HaveCountGreaterThan(50, "the scan must find the module handlers");

        var unresolved = handlers
            .SelectMany(handler => handler.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .Take(1)
                .SelectMany(c => c.GetParameters())
                .Where(p => !p.HasDefaultValue && !services.IsService(p.ParameterType))
                .Select(p => $"{handler.Name}({p.ParameterType.Name} {p.Name})"))
            .ToList();

        unresolved.Should().BeEmpty("a handler with an unregistered dependency fails on its first request");
    }
}
