using System.Reflection;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Data;
using Shared.Core.Messaging.Sagas;
using Shared.Core.MultiTenancy;

namespace Api.Tests.Architecture;

/// <summary>
/// RULE (ADR-0023): saga state is durable. A saga is registered only through
/// <see cref="DurableSagaExtensions.AddDurableSagaStateMachine{TMachine,TState,TDbContext}"/> in its module's
/// <c>ConfigureMessaging</c>, so MassTransit's EF Core repository keeps its state in the module's DbContext.
/// MassTransit's own registration methods default to the IN-MEMORY repository: state lost on every restart and
/// different on every replica, and nothing fails until production.
/// </summary>
public sealed class SagaArchitectureTests
{
    /// <summary>MassTransit registration methods that bypass the durable helper.</summary>
    private static readonly HashSet<string> ForbiddenRegistrations =
    [
        "AddSagaStateMachine", "AddSagaStateMachines", "AddSaga", "AddSagas", "AddSagasFromNamespaceContaining",
        "AddSagaStateMachinesFromNamespaceContaining", "SetInMemorySagaRepositoryProvider", "InMemoryRepository",
        "SetSagaRepositoryProvider"
    ];

    /// <summary>Every module plus the shared kernel (where the bus is registered).</summary>
    private static readonly string[] ScannedAssemblies =
        [.. ModuleBoundaryTests.Modules, "Shared.Contracts", "Shared.Core", "Shared.Messaging"];

    [Fact]
    public void Sagas_are_registered_only_through_AddDurableSagaStateMachine()
    {
        var violations = ScannedAssemblies
            .Select(Assembly.Load)
            .SelectMany(a => a.GetTypes())
            .Where(t => t != typeof(DurableSagaExtensions))
            .SelectMany(t => ForbiddenCalls(t).Select(call => $"{t.FullName} calls {call}"))
            .Distinct()
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        violations.Should().BeEmpty(
            "register sagas with bus.AddDurableSagaStateMachine<TMachine, TState, TDbContext>() in the module's " +
            "ConfigureMessaging; MassTransit's own methods default to the in-memory saga repository");
    }

    [Fact]
    public void The_registration_detector_sees_a_direct_MassTransit_saga_registration()
    {
        // Guards the guard: if the IL scan broke, the main rule would pass vacuously.
        ForbiddenCalls(typeof(DirectRegistration)).Should().Contain("MassTransit AddSagaStateMachine");
        ForbiddenCalls(typeof(DurableRegistration)).Should().BeEmpty("the helper is the allowed path");
    }

    [Fact]
    public void Every_saga_state_is_mapped_in_a_module_DbContext_and_resolves_a_durable_repository()
    {
        var states = ModuleBoundaryTests.Modules
            .Select(Assembly.Load)
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(SagaStateMachineInstance).IsAssignableFrom(t))
            .ToList();

        using var scope = RealHost.Services.CreateScope();
        using var host = TenantScope.BeginHostScope();
        var mapped = scope.ServiceProvider.GetServices<IDbContext>().OfType<DbContext>()
            .SelectMany(context => context.Model.GetEntityTypes().Select(entity => entity.ClrType))
            .ToHashSet();

        var violations = new List<string>();
        foreach (var state in states)
        {
            if (!mapped.Contains(state))
                violations.Add($"{state.FullName}: not mapped in any module DbContext (apply a SagaStateConfiguration)");

            var repository = scope.ServiceProvider.GetService(typeof(ISagaRepository<>).MakeGenericType(state));
            if (repository is null)
                violations.Add($"{state.FullName}: no saga repository (register it in the module's ConfigureMessaging)");
            else if (repository.GetType().FullName!.Contains("InMemory", StringComparison.Ordinal))
                violations.Add($"{state.FullName}: in-memory saga repository {repository.GetType().Name}");
        }

        violations.Should().BeEmpty("saga state must survive restarts and be shared by every replica (ADR-0023)");
    }

    private static IEnumerable<string> ForbiddenCalls(Type type) =>
        IlCalls.AllCalls(type)
            .Where(m => ForbiddenRegistrations.Contains(m.Name) &&
                        m.DeclaringType?.Assembly.GetName().Name?.StartsWith("MassTransit", StringComparison.Ordinal) == true)
            .Select(m => $"MassTransit {m.Name}")
            .Distinct();

    // ----- fixtures for the self-test (never registered in the host) -----

    private sealed class ProbeState : SagaStateMachineInstance
    {
        public Guid CorrelationId { get; set; }
        public string CurrentState { get; set; } = default!;
    }

    private sealed class ProbeStateMachine : MassTransitStateMachine<ProbeState>
    {
        public ProbeStateMachine() => InstanceState(x => x.CurrentState);
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);

    private static class DirectRegistration
    {
        public static void Register(IBusRegistrationConfigurator bus) =>
            bus.AddSagaStateMachine<ProbeStateMachine, ProbeState>();
    }

    private static class DurableRegistration
    {
        public static void Register(IBusRegistrationConfigurator bus) =>
            bus.AddDurableSagaStateMachine<ProbeStateMachine, ProbeState, ProbeDbContext>();
    }
}
