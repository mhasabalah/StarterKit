using System.Reflection;
using System.Text.RegularExpressions;
using MassTransit;
using Shared.Contracts.Mediation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Events;
using Shared.Contracts.Messaging;
using Shared.Core.Messaging.Outbox;

namespace Api.Tests.Architecture;

/// <summary>
/// Rules of the integration-event / transactional-outbox design (see <c>wiki/Architecture/Messaging.md</c>):
/// <list type="bullet">
/// <item>An integration event is a published CONTRACT: it lives in a <c>*.Contracts</c> assembly, pins its wire name with
/// <c>[MessageUrn("&lt;module&gt;.&lt;event&gt;.vN")]</c> (unique, versioned, never derived from the CLR name) and is
/// never ALSO an in-process mediator notification / domain event.</item>
/// <item>MassTransit consumers belong to the module that reacts, i.e. to a module IMPLEMENTATION assembly.</item>
/// <item>Every context that maps an outbox table is registered for delivery (<c>AddOutbox&lt;TContext&gt;()</c>),
/// otherwise its rows are written and never published.</item>
/// <item>Contracts stay light: they reference the shared contracts only, so any module (or a future external service)
/// can reference them without pulling in infrastructure.</item>
/// </list>
/// </summary>
public sealed class MessagingArchitectureTests
{
    private const string UrnPrefix = "urn:message:";

    private static readonly string[] Modules = ModuleBoundaryTests.Modules;

    /// <summary>Contracts assemblies the integration-event rules apply to (module contracts + ports).</summary>
    private static readonly string[] ContractsAssemblies =
        [.. Modules.Select(m => $"{m}.Contracts"), .. ModuleBoundaryTests.Ports];

    /// <summary>
    /// Contracts allowed to reference Shared.Core / Shared.Messaging. Each entry states WHY; do not add new ones lightly.
    /// </summary>
    private static readonly Dictionary<string, string> InfrastructureReferenceExceptions = new()
    {
        ["Tenancy.Contracts"] = "its commands/DTOs use Shared.Core.MultiTenancy types (TenantInfo, TenantPermissions, ...)"
    };

    private static readonly string[] InfrastructureAssemblies = ["Shared.Core", "Shared.Messaging"];

    private static readonly Regex UrnName = new(@"^[a-z]+\.[a-z0-9-]+\.v[0-9]+$", RegexOptions.Compiled);

    // ----- integration events ----------------------------------------------------------------------------------------

    [Fact]
    public void Integration_events_exist_and_are_found_by_the_scan()
    {
        IntegrationEvents().Select(t => t.Name).Should().Contain(
        [
#if (SAMPLE)
            "ProductPublishedIntegrationEvent",
#endif
#if (SAMPLE && AGENTIC)
            "ShipmentReadyForClearanceIntegrationEvent",
#endif
#if (AGENTIC)
            "AgentRunCompletedIntegrationEvent", "AgentRunFailedIntegrationEvent",
#endif
#if (SEARCH)
            "SearchDocumentsChangedIntegrationEvent",
#endif
            "UserAvatarChangedIntegrationEvent", "AttachmentDeletedIntegrationEvent", "UserDeletedIntegrationEvent",
            "UserSessionsRevokedIntegrationEvent",
            "SubscriptionActivatedIntegrationEvent", "SubscriptionExpiredIntegrationEvent",
            "DataImportCompletedIntegrationEvent"
        ]);
    }

    [Fact]
    public void Integration_events_live_in_contracts_assemblies_only()
    {
        var violations = AllSolutionAssemblies()
            .SelectMany(Types)
            .Where(IsConcreteIntegrationEvent)
            .Where(t => !t.Assembly.GetName().Name!.EndsWith(".Contracts", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToList();

        violations.Should().BeEmpty("a published event is a contract: it belongs to <Module>.Contracts, never to an implementation");
    }

    [Fact]
    public void Integration_events_live_under_an_IntegrationEvents_namespace()
    {
        var violations = IntegrationEvents()
            .Where(t => !(t.Namespace ?? string.Empty).Split('.').Contains("IntegrationEvents"))
            .Select(t => t.FullName!)
            .ToList();

        violations.Should().BeEmpty("convention: <Module>.Contracts/<Area>/IntegrationEvents/");
    }

    [Fact]
    public void Integration_events_pin_a_versioned_urn_prefixed_with_their_module()
    {
        var violations = new List<string>();
        foreach (var type in IntegrationEvents())
        {
            var attribute = type.GetCustomAttribute<MessageUrnAttribute>(inherit: false);
            if (attribute is null)
            {
                violations.Add($"{type.FullName}: no [MessageUrn]");
                continue;
            }

            var urn = attribute.Urn.ToString();
            if (!urn.StartsWith(UrnPrefix, StringComparison.Ordinal))
            {
                violations.Add($"{type.FullName}: unexpected urn format '{urn}' (expected the default '{UrnPrefix}' prefix)");
                continue;
            }

            var name = urn[UrnPrefix.Length..]; // the part we control
            if (!UrnName.IsMatch(name))
            {
                violations.Add($"{type.FullName}: '{name}' does not match <module>.<event-name>.v<N> (lower-case, kebab-case)");
                continue;
            }

            var module = type.Assembly.GetName().Name!.Replace(".Contracts", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (!name.StartsWith(module + ".", StringComparison.Ordinal))
                violations.Add($"{type.FullName}: urn '{name}' must start with its module '{module}.'");
        }

        violations.Should().BeEmpty();
    }

    [Fact]
    public void Integration_event_urns_are_unique()
    {
        var duplicates = IntegrationEvents()
            .Where(t => t.GetCustomAttribute<MessageUrnAttribute>(inherit: false) is not null)
            .GroupBy(t => t.GetCustomAttribute<MessageUrnAttribute>(inherit: false)!.Urn.ToString(), StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(t => t.FullName))}")
            .ToList();

        duplicates.Should().BeEmpty("the urn is the wire identity: two events sharing one would be indistinguishable in the outbox");
    }

    [Fact]
    public void The_outbox_resolves_every_event_it_could_be_asked_to_publish()
    {
        // The runtime view of the same rule (also detects duplicates): every urn maps back to exactly its type.
        var map = IntegrationEventTypes.Scan(ContractsAssemblies.Select(Assembly.Load));

        map.Values.Should().BeEquivalentTo(IntegrationEvents());
    }

    [Fact]
    public void Integration_events_are_not_in_process_notifications()
    {
        var violations = IntegrationEvents()
            .Where(t => typeof(INotification).IsAssignableFrom(t) || typeof(IDomainEvent).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .ToList();

        violations.Should().BeEmpty(
            "an integration event goes through the outbox and the bus; implementing IDomainEvent/INotification " +
            "would also publish it in-process and handle it twice");
    }

    // ----- consumers -------------------------------------------------------------------------------------------------

    [Fact]
    public void Consumers_exist_and_are_found_by_the_scan()
    {
        Consumers().Select(t => t.Name).Should().Contain(["UserAvatarChangedConsumer", "TenantBillingStatusConsumer", "UserDeletedAttachmentConsumer", "UserDeletedTenancyConsumer", "UserDeletedNotificationConsumer", "UserSessionsRevokedConsumer"]);
#if (SEARCH)
        Consumers().Select(t => t.Name).Should().Contain("SearchDocumentsChangedConsumer");
#endif
    }

    [Fact]
    public void Consumers_live_in_module_implementation_assemblies_only()
    {
        var violations = AllSolutionAssemblies()
            .SelectMany(Types)
            .Where(IsConsumer)
            .Where(t => !Modules.Contains(t.Assembly.GetName().Name))
            .Select(t => $"{t.FullName} ({t.Assembly.GetName().Name})")
            .ToList();

        violations.Should().BeEmpty(
            "the module that reacts owns the consumer; contracts and the shared kernel must stay free of behavior");
    }

    // ----- outbox registration ---------------------------------------------------------------------------------------

    [Fact]
    public void Contexts_that_map_an_outbox_are_registered_for_delivery()
    {
        var registered = ModuleRegistrations().ContextTypes.ToHashSet();
        var mapping = ModuleDbContexts()
            .Where(t => MapsOutbox(t))
            .ToList();

        mapping.Select(t => t.Name).Should().Contain([
#if (SAMPLE)
                "CatalogDbContext",
#endif
#if (SAMPLE && AGENTIC)
                "ShippingDbContext",
#endif
#if (AGENTIC)
                "AgenticDbContext",
#endif
                "AttachmentDbContext", "SubscriptionsDbContext", "DataTransferDbContext"],
            "these modules raise integration events");
        mapping.Where(t => !registered.Contains(t)).Select(t => t.FullName)
            .Should().BeEmpty("a context with an outbox table needs services.AddOutbox<TContext>(), or its rows are never published");
    }

    [Fact]
    public void Registered_outbox_contexts_really_map_an_outbox()
    {
        var unmapped = ModuleRegistrations().ContextTypes
            .Where(t => !MapsOutbox(t))
            .Select(t => t.FullName)
            .ToList();

        unmapped.Should().BeEmpty("AddOutbox<TContext>() without builder.AddOutbox()/AddTenantOutbox() makes the sweep fail at runtime");
    }

    // ----- contracts stay light --------------------------------------------------------------------------------------

    public static TheoryData<string> ContractsNames => new(ContractsAssemblies);

    [Theory]
    [MemberData(nameof(ContractsNames))]
    public void Contracts_do_not_reference_shared_infrastructure_unless_explicitly_allowed(string contracts)
    {
        var references = Assembly.Load(contracts).GetReferencedAssemblies().Select(a => a.Name!).ToHashSet();
        var offending = references.Intersect(InfrastructureAssemblies).ToList();

        if (InfrastructureReferenceExceptions.TryGetValue(contracts, out var reason))
        {
            reason.Should().NotBeNullOrWhiteSpace();
            return;
        }

        offending.Should().BeEmpty($"{contracts} must reference Shared.Contracts only (Shared.Core / Shared.Messaging are infrastructure)");
    }

    [Fact]
    public void Infrastructure_reference_exceptions_are_still_needed()
    {
        foreach (var (contracts, reason) in InfrastructureReferenceExceptions)
        {
            Assembly.Load(contracts).GetReferencedAssemblies().Select(a => a.Name!)
                .Intersect(InfrastructureAssemblies).Should().NotBeEmpty($"stale exception for {contracts} ({reason}): remove it");
        }
    }

    // ----- helpers ---------------------------------------------------------------------------------------------------

    private static IEnumerable<Assembly> AllSolutionAssemblies() =>
        Modules.SelectMany(m => new[] { m, $"{m}.Contracts" })
            .Concat(ModuleBoundaryTests.Ports)
            .Concat(["Shared.Contracts", "Shared.Core", "Shared.Messaging", "Api"])
            .Select(Assembly.Load);

    private static IEnumerable<Type> Types(Assembly assembly)
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

    private static bool IsConcreteIntegrationEvent(Type t) =>
        t is { IsClass: true, IsAbstract: false } && typeof(IIntegrationEvent).IsAssignableFrom(t);

    private static List<Type> IntegrationEvents() =>
        ContractsAssemblies.Select(Assembly.Load).SelectMany(Types).Where(IsConcreteIntegrationEvent).ToList();

    private static bool IsConsumer(Type t) =>
        t is { IsClass: true, IsAbstract: false } &&
        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>));

    private static List<Type> Consumers() => Modules.Select(Assembly.Load).SelectMany(Types).Where(IsConsumer).ToList();

    private static List<Type> ModuleDbContexts() =>
        Modules.Select(Assembly.Load).SelectMany(Types)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(DbContext).IsAssignableFrom(t))
            .ToList();

    /// <summary>Builds the EF model offline (no connection is opened) and looks for an outbox entity.</summary>
    private static bool MapsOutbox(Type contextType)
    {
        var optionsType = typeof(DbContextOptions<>).MakeGenericType(contextType);
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        builder.UseNpgsql("Host=localhost;Database=architecture-test").UseSnakeCaseNamingConvention();
        var context = (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
        using (context)
        {
            return context.FindOutboxMessageType() is not null;
        }
    }

    /// <summary>The outbox registry after every module registered itself (no provider is built, nothing connects).</summary>
    private static OutboxRegistry ModuleRegistrations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = "Host=localhost;Database=architecture-test;Username=u;Password=p"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        foreach (var module in Modules)
        {
            var registration = Assembly.Load(module).GetTypes()
                .Where(t => t is { IsClass: true, IsAbstract: true, IsSealed: true } && t.Name == $"{module}Module")
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Single(m => m.Name == $"Add{module}Module");
            var args = registration.GetParameters()
                .Select(p => p.ParameterType == typeof(IServiceCollection) ? services
                    : p.ParameterType == typeof(IConfiguration) ? (object)configuration
                    : p.HasDefaultValue ? p.DefaultValue
                    : throw new InvalidOperationException($"Cannot call {registration.Name}: unknown parameter {p.Name}"))
                .ToArray();
            registration.Invoke(null, args);
        }

        return (OutboxRegistry)services.Single(d => d.ServiceType == typeof(OutboxRegistry)).ImplementationInstance!;
    }
}
