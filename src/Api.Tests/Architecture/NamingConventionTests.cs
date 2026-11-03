using System.Reflection;
using Carter;
using FluentValidation;
using MediatR;
using Shared.Contracts.CQRS;
using Shared.Contracts.Events;

namespace Api.Tests.Architecture;

/// <summary>
/// Enforces the "Strict Naming Conventions" table in CLAUDE.md, so new code (human- or AI-written)
/// cannot drift. Every failure message lists the offending types.
/// </summary>
public sealed class NamingConventionTests
{
    private static readonly Assembly[] ModuleAssemblies = ModuleBoundaryTests.Modules
        .SelectMany(m => new[] { m, $"{m}.Contracts" })
        .Concat(ModuleBoundaryTests.Ports)
        .Select(Assembly.Load)
        .ToArray();

    private static IEnumerable<Type> ConcreteTypes => ModuleAssemblies
        .SelectMany(SafeGetTypes)
        .Where(t => t is { IsClass: true, IsAbstract: false } && !IsCompilerGenerated(t));

    [Fact]
    public void Command_and_query_handlers_end_with_Handler()
    {
        var offenders = ConcreteTypes
            .Where(t => Implements(t, typeof(IRequestHandler<,>)) || Implements(t, typeof(IRequestHandler<>)))
            .Where(t => !t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(t => t.FullName);

        offenders.Should().BeEmpty("handlers are named <Action><X>Handler");
    }

    [Fact]
    public void Commands_end_with_Command()
    {
        var offenders = ConcreteTypes
            .Where(t => Implements(t, typeof(ICommand<>)))
            .Where(t => !t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Select(t => t.FullName);

        offenders.Should().BeEmpty("commands are named <Action><X>Command");
    }

    [Fact]
    public void Queries_end_with_Query()
    {
        var offenders = ConcreteTypes
            .Where(t => Implements(t, typeof(IQuery<>)))
            .Where(t => !t.Name.EndsWith("Query", StringComparison.Ordinal))
            .Select(t => t.FullName);

        offenders.Should().BeEmpty("queries are named Get<X>sQuery / Get<X>ByIdQuery");
    }

    [Fact]
    public void Validators_end_with_Validator()
    {
        var offenders = ConcreteTypes
            .Where(t => InheritsGeneric(t, typeof(AbstractValidator<>)))
            .Where(t => !t.Name.EndsWith("Validator", StringComparison.Ordinal))
            .Select(t => t.FullName);

        offenders.Should().BeEmpty("validators are named <Command>Validator");
    }

    [Fact]
    public void Domain_events_end_with_Event()
    {
        var offenders = ConcreteTypes
            .Where(t => typeof(IDomainEvent).IsAssignableFrom(t))
            .Where(t => !t.Name.EndsWith("Event", StringComparison.Ordinal))
            .Select(t => t.FullName);

        offenders.Should().BeEmpty("domain events are named <X><PastTenseVerb>Event");
    }

    [Fact]
    public void Carter_modules_end_with_EndPoint_or_Module()
    {
        var offenders = ConcreteTypes
            .Where(t => typeof(ICarterModule).IsAssignableFrom(t))
            .Where(t => !t.Name.EndsWith("EndPoint", StringComparison.Ordinal) &&
                        !t.Name.EndsWith("Module", StringComparison.Ordinal))
            .Select(t => t.FullName);

        offenders.Should().BeEmpty("endpoints are named <Action><X>EndPoint (grouped route modules: <X>Module)");
    }

    [Fact]
    public void Handlers_live_in_implementation_assemblies_not_contracts()
    {
        var offenders = ModuleAssemblies
            .Where(a => a.GetName().Name!.EndsWith(".Contracts", StringComparison.Ordinal))
            .SelectMany(SafeGetTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        (Implements(t, typeof(IRequestHandler<,>)) || Implements(t, typeof(IRequestHandler<>))))
            .Select(t => t.FullName);

        offenders.Should().BeEmpty(".Contracts holds commands/queries/DTOs only; handlers belong to the module");
    }

    private static bool Implements(Type type, Type openGenericInterface) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface);

    private static bool InheritsGeneric(Type type, Type openGenericBase)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == openGenericBase)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCompilerGenerated(Type t) =>
        t.Name.Contains('<') || t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false);

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
