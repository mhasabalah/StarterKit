using Shared.Contracts.Messaging;
using Shared.Core.Messaging.Outbox;
using Webhooks.Domains.Deliveries.Consumers;
using Webhooks.Domains.Endpoints.Models;

namespace Webhooks.Services;

public sealed record PublicWebhookEventInfo(string Type, Type ClrType, string? Description);

/// <summary>
/// The public event-type catalog: every integration event marked <see cref="PublicWebhookEventAttribute"/> (found by
/// <see cref="IntegrationEventTypes.Scan"/>) that this module actually forwards. Customers subscribe by the wire name,
/// e.g. <c>catalog.product-published.v1</c>.
/// </summary>
public interface IWebhookEventCatalog
{
    IReadOnlyList<PublicWebhookEventInfo> Events { get; }

    bool IsKnown(string type);
}

public sealed class WebhookEventCatalog : IWebhookEventCatalog
{
    private const string UrnPrefix = "urn:message:";

    private readonly Lazy<IReadOnlyList<PublicWebhookEventInfo>> _events = new(Build);

    public IReadOnlyList<PublicWebhookEventInfo> Events => _events.Value;

    public bool IsKnown(string type) =>
        type == WebhookEndpoint.AllEvents || Events.Any(e => e.Type == type);

    /// <summary>The customer-facing name of an integration event (its <c>[MessageUrn]</c> without the transport prefix).</summary>
    public static string TypeOf(Type eventType)
    {
        var urn = IntegrationEventTypes.NameOf(eventType);
        return urn.StartsWith(UrnPrefix, StringComparison.Ordinal) ? urn[UrnPrefix.Length..] : urn;
    }

    /// <summary>Marked events that no forwarding consumer covers (a programming error; a unit test pins it to empty).</summary>
    public static IReadOnlyList<Type> MarkedWithoutConsumer() =>
        Marked().Keys.Except(ForwardedEventTypes()).ToList();

    private static IReadOnlyList<PublicWebhookEventInfo> Build()
    {
        var forwarded = ForwardedEventTypes().ToHashSet();
        return Marked()
            .Where(kv => forwarded.Contains(kv.Key))
            .Select(kv => new PublicWebhookEventInfo(TypeOf(kv.Key), kv.Key, kv.Value))
            .OrderBy(e => e.Type, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Marked event type -> description. Reflecting the forwarding consumers first loads their contracts assemblies.</summary>
    private static Dictionary<Type, string?> Marked()
    {
        _ = ForwardedEventTypes().ToList();
        var marked = new Dictionary<Type, string?>();
        foreach (var type in IntegrationEventTypes.Scan().Values)
        {
            var attribute = type.GetCustomAttribute<PublicWebhookEventAttribute>(inherit: false);
            if (attribute is not null) marked[type] = attribute.Description;
        }

        return marked;
    }

    /// <summary>The event types the concrete <c>WebhookForwardingConsumer&lt;T&gt;</c> subclasses forward.</summary>
    public static IEnumerable<Type> ForwardedEventTypes() =>
        typeof(WebhookEventCatalog).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true })
            .Select(BaseForwardingType)
            .Where(b => b is not null)
            .Select(b => b!.GetGenericArguments()[0]);

    private static Type? BaseForwardingType(Type type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(WebhookForwardingConsumer<>)) return t;
        }

        return null;
    }
}
