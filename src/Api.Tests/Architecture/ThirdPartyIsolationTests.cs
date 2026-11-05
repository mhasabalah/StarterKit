using System.Reflection;

namespace Api.Tests.Architecture;

/// <summary>
/// Keeps vendor libraries behind their seams, so a licensing or support change is a one-adapter job
/// (wiki/Reference/Third-Party-Licenses.md, wiki/Decisions/0007-owned-mediator-port.md):
/// <list type="bullet">
/// <item>The in-process mediator is OURS (<c>Shared.Contracts.Mediation</c>); nothing references MediatR.</item>
/// <item>Imaging stays behind <c>IImageProcessingService</c> in Attachment (SkiaSharp adapter); the revenue-gated
/// ImageSharp is not referenced at all; QuestPDF (revenue-gated) stays in Subscriptions.</item>
/// <item>Contracts may carry the <c>[MessageUrn]</c> attribute (MassTransit.Abstractions) but never the bus itself.</item>
/// <item>FCM (FirebaseAdmin / Google.Apis) stays behind <c>IPushProvider</c> in Notification, and so does the SignalR
/// Redis backplane.</item>
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
    public void The_imaging_library_is_used_only_by_the_Attachment_module() =>
        Production.Where(a => References(a).Any(r => r.StartsWith("SkiaSharp", StringComparison.Ordinal)))
            .Should().Equal(["Attachment"], "SkiaSharp sits behind IImageProcessingService (one adapter file)");

    [Theory]
    [MemberData(nameof(ProductionAssemblies))]
    public void Nothing_references_ImageSharp(string assembly) =>
        References(assembly).Should().NotContain(r => r.StartsWith("SixLabors.", StringComparison.Ordinal),
            "ImageSharp needs a license key at build time and is revenue-gated; add it only as an opt-in adapter " +
            "(wiki/Attachment/Image-Processing.md)");

    [Fact]
    public void QuestPdf_is_used_only_by_the_Subscriptions_module() =>
        Production.Where(a => References(a).Contains("QuestPDF"))
            .Should().Equal(["Subscriptions"], "QuestPDF is community-licensed below a revenue threshold; keep it in one place");

    [Fact]
    public void FirebaseAdmin_is_used_only_by_the_Notification_module() =>
        Production.Where(a => References(a).Any(r => r.StartsWith("FirebaseAdmin", StringComparison.Ordinal)
                                                    || r.StartsWith("Google.Apis", StringComparison.Ordinal)))
            .Should().Equal(["Notification"], "push delivery stays behind IPushProvider; swapping FCM must touch one module");

    [Fact]
    public void SignalR_server_types_stay_in_the_Notification_module() =>
        Production.Where(a => References(a).Contains("Microsoft.AspNetCore.SignalR.StackExchangeRedis"))
            .Should().BeSubsetOf(["Notification"], "the hub and its backplane are Notification infrastructure");

    [Theory]
    [MemberData(nameof(ContractAssemblies))]
    public void Contracts_never_reference_the_message_bus(string assembly) =>
        References(assembly).Should().NotContain(r => r == "MassTransit" || r.StartsWith("MassTransit.", StringComparison.Ordinal)
                                                     && r != "MassTransit.Abstractions",
            "contracts carry plain event types (+ [MessageUrn]); the bus belongs to Shared.Messaging and the modules");

    private static HashSet<string> References(string assemblyName) =>
        Assembly.Load(assemblyName).GetReferencedAssemblies().Select(a => a.Name!).ToHashSet(StringComparer.Ordinal);
}
