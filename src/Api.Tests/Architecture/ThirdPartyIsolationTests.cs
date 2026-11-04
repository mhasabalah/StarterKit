using System.Reflection;

namespace Api.Tests.Architecture;

/// <summary>
/// Keeps vendor libraries behind their seams, so a licensing or support change is a one-adapter job
/// (wiki/Reference/Third-Party-Licenses.md, wiki/Decisions/0007-owned-mediator-port.md):
/// <list type="bullet">
/// <item>The in-process mediator is OURS (<c>Shared.Contracts.Mediation</c>); nothing references MediatR.</item>
/// <item>Revenue-gated libraries stay in the one module that owns them: ImageSharp in Attachment, QuestPDF in
/// Subscriptions.</item>
/// <item>Contracts may carry the <c>[MessageUrn]</c> attribute (MassTransit.Abstractions) but never the bus itself.</item>
/// </list>
/// </summary>
public sealed class ThirdPartyIsolationTests
{
    private static readonly string[] Production =
        ModuleBoundaryTests.Modules
            .SelectMany(m => new[] { m, $"{m}.Contracts" })
            .Concat(ModuleBoundaryTests.Ports)
            .Concat(["Shared.Contracts", "Shared.Core", "Shared.Messaging", "Api"])
            .ToArray();

    public static TheoryData<string> ProductionAssemblies => new(Production);

    public static TheoryData<string> ContractAssemblies => new(
        ModuleBoundaryTests.Modules.Select(m => $"{m}.Contracts")
            .Concat(ModuleBoundaryTests.Ports)
            .Append("Shared.Contracts"));

    [Theory]
    [MemberData(nameof(ProductionAssemblies))]
    public void Nothing_references_MediatR(string assembly) =>
        References(assembly).Should().NotContain(r => r.StartsWith("MediatR", StringComparison.Ordinal),
            "requests and notifications go through the owned port in Shared.Contracts.Mediation");

    /// <summary>
    /// Notifications are dispatched by their exact runtime type (as MediatR 12 did on Microsoft DI): a handler for a
    /// base class or interface (<c>INotificationHandler&lt;IDomainEvent&gt;</c>) would compile and never run.
    /// </summary>
    [Fact]
    public void Notification_handlers_target_concrete_event_types()
    {
        var offenders = Production
            .SelectMany(a => Assembly.Load(a).GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Shared.Contracts.Mediation.INotificationHandler<>))
                .Select(i => i.GetGenericArguments()[0])
                .Where(e => !e.IsGenericParameter && (e.IsInterface || e.IsAbstract))
                .Select(e => $"{t.FullName} handles {e.Name}"))
            .ToList();

        offenders.Should().BeEmpty("a handler for an abstract/interface event is never invoked; handle each concrete event");
    }

    [Fact]
    public void ImageSharp_is_used_only_by_the_Attachment_module() =>
        Production.Where(a => References(a).Any(r => r.StartsWith("SixLabors.", StringComparison.Ordinal)))
            .Should().Equal(["Attachment"], "ImageSharp sits behind IImageProcessingService (Split License, revenue-gated)");

    [Fact]
    public void QuestPdf_is_used_only_by_the_Subscriptions_module() =>
        Production.Where(a => References(a).Contains("QuestPDF"))
            .Should().Equal(["Subscriptions"], "QuestPDF is community-licensed below a revenue threshold; keep it in one place");

    [Theory]
    [MemberData(nameof(ContractAssemblies))]
    public void Contracts_never_reference_the_message_bus(string assembly) =>
        References(assembly).Should().NotContain(r => r == "MassTransit" || r.StartsWith("MassTransit.", StringComparison.Ordinal)
                                                     && r != "MassTransit.Abstractions",
            "contracts carry plain event types (+ [MessageUrn]); the bus belongs to Shared.Messaging and the modules");

    private static HashSet<string> References(string assemblyName) =>
        Assembly.Load(assemblyName).GetReferencedAssemblies().Select(a => a.Name!).ToHashSet(StringComparer.Ordinal);
}
