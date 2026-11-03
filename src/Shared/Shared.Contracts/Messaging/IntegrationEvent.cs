namespace Shared.Contracts.Messaging;

/// <summary>
/// A fact a module publishes for OTHER modules to react to eventually (at-least-once, asynchronously, through the
/// transactional outbox and the message bus). The publisher never waits for, or fails because of, the reaction.
/// <para>
/// Rules (enforced by <c>Api.Tests/Architecture/MessagingArchitectureTests</c>):
/// </para>
/// <list type="bullet">
/// <item>Lives in the publishing module's <c>.Contracts</c> project, under <c>.../IntegrationEvents/</c>.</item>
/// <item>Carries a stable, versioned name: <c>[MessageUrn("&lt;module&gt;.&lt;event-name&gt;.v1")]</c>. Renaming or moving the
/// class must never change it - messages already in the outbox or a queue are matched by this name.</item>
/// <item>Evolves by ADDING optional properties only. A breaking change is a new type with a new <c>.vN</c> urn.</item>
/// <item>Carries ids and a minimal projection - never an aggregate or an EF entity.</item>
/// </list>
/// Raise it from the aggregate with <c>AddIntegrationEvent(...)</c>; it is written to the outbox in the same
/// transaction as the aggregate. See <c>wiki/Architecture/Messaging.md</c>.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>Unique id; also the transport message id (consumers may de-duplicate on it).</summary>
    Guid EventId { get; }

    /// <summary>When the fact happened (UTC).</summary>
    DateTime OccurredOn { get; }
}

/// <summary>
/// Convenience base for <see cref="IIntegrationEvent"/>. <see cref="EventId"/> and <see cref="OccurredOn"/> (UTC) are
/// fixed at construction, so every read, serialization round trip and consumer retry sees the same values.
/// The tenant is NOT part of the payload: it travels as a transport header and is restored as the consumer's
/// ambient tenant scope.
/// </summary>
public abstract class IntegrationEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
}
