using System.Reflection;
using System.Reflection.Emit;
using MassTransit;
using Shared.Contracts.Messaging;
using Shared.Core.Messaging.Outbox;

namespace Shared.Core.Tests.Messaging;

public sealed class IntegrationEventTypesTests
{
    private const string ThingUrn = "urn:message:shared-tests.thing-happened.v1";

    [Fact]
    public void NameOf_returns_the_pinned_urn_not_the_clr_name()
    {
        IntegrationEventTypes.NameOf(typeof(ThingHappenedIntegrationEvent)).Should().Be(ThingUrn);
    }

    [Fact]
    public void NameOf_refuses_an_event_without_a_urn()
    {
        var act = () => IntegrationEventTypes.NameOf(typeof(UnpinnedIntegrationEvent));

        act.Should().Throw<InvalidOperationException>().Which.Message
            .Should().Contain(nameof(UnpinnedIntegrationEvent)).And.Contain("MessageUrn");
    }

    [Fact]
    public void NameOf_refuses_a_type_that_is_not_an_integration_event()
    {
        var act = () => IntegrationEventTypes.NameOf(typeof(string));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("not an IIntegrationEvent");
    }

    [Fact]
    public void Resolve_maps_a_stored_urn_back_to_the_type()
    {
        IntegrationEventTypes.Resolve(ThingUrn).Should().Be<ThingHappenedIntegrationEvent>();
    }

    [Fact]
    public void Resolve_returns_null_for_an_unknown_urn_and_ignores_types_without_urn()
    {
        IntegrationEventTypes.Resolve("urn:message:shared-tests.never-existed.v1").Should().BeNull();
        IntegrationEventTypes.Resolve(nameof(UnpinnedIntegrationEvent)).Should().BeNull();
    }

    [Fact]
    public void Scan_finds_the_pinned_events_of_an_assembly_and_skips_unpinned_ones()
    {
        var map = IntegrationEventTypes.Scan([typeof(ThingHappenedIntegrationEvent).Assembly]);

        map.Should().ContainKey(ThingUrn).WhoseValue.Should().Be<ThingHappenedIntegrationEvent>();
        map.Values.Should().NotContain(typeof(UnpinnedIntegrationEvent));
        map.Values.Should().OnlyContain(t => !t.IsAbstract);
    }

    [Fact]
    public void Scan_detects_two_events_sharing_a_urn()
    {
        var (first, second) = EmitEventsWithSameUrn("shared-tests.duplicate.v1");

        var act = () => IntegrationEventTypes.Scan([new FixedTypesAssembly(first, second)]);

        act.Should().Throw<InvalidOperationException>().Which.Message
            .Should().Contain("share the urn").And.Contain("shared-tests.duplicate.v1")
            .And.Contain(first.FullName!).And.Contain(second.FullName!);
    }

    [Fact]
    public void Scan_accepts_distinct_urns()
    {
        var (first, _) = EmitEventsWithSameUrn("shared-tests.distinct-a.v1");
        var (second, _) = EmitEventsWithSameUrn("shared-tests.distinct-b.v1");

        var map = IntegrationEventTypes.Scan([new FixedTypesAssembly(first, second)]);

        map.Should().HaveCount(2);
    }

    /// <summary>
    /// Emitted in a dynamic assembly (skipped by the AppDomain-wide scan the relay uses), so a deliberate duplicate
    /// never poisons <see cref="IntegrationEventTypes.Resolve"/> for the other tests.
    /// </summary>
    private static (Type First, Type Second) EmitEventsWithSameUrn(string urn)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("dup-events-" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("main");
        var ctor = typeof(MessageUrnAttribute).GetConstructor([typeof(string), typeof(bool)])!;

        Type Define(string name)
        {
            var builder = module.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                typeof(IntegrationEvent));
            builder.DefineDefaultConstructor(MethodAttributes.Public);
            builder.SetCustomAttribute(new CustomAttributeBuilder(ctor, [urn, true]));
            return builder.CreateType();
        }

        return (Define("FirstEvent" + Guid.NewGuid().ToString("N")), Define("SecondEvent" + Guid.NewGuid().ToString("N")));
    }

    private sealed class FixedTypesAssembly(params Type[] types) : Assembly
    {
        public override bool IsDynamic => false;

        public override Type[] GetTypes() => types;
    }
}
