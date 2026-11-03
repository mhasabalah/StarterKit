using Shared.Core.MultiTenancy;

namespace Shared.Core.Messaging.Outbox;

/// <summary>
/// One integration event waiting to be (or already) published. Written by <see cref="OutboxInterceptor"/> in the same
/// SaveChanges - so the same database transaction - as the aggregate that raised it; delivered by
/// <see cref="OutboxRelay"/> (right after the commit, and by <see cref="OutboxSweepJob"/> as the safety net).
/// <para>
/// Infrastructure only: never query or modify it from feature code. Mapped per module context (own schema) by
/// <see cref="OutboxModelBuilderExtensions"/>; two concrete row types exist because tenant-data contexts require every
/// entity to be tenant-owned (filters, RLS, tenant move/purge) while host contexts may carry no tenant.
/// </para>
/// </summary>
public abstract class OutboxMessageBase
{
    public const int MessageTypeMaxLength = 256;
    public const int LastErrorMaxLength = 2000;

    /// <summary>= the integration event's <c>EventId</c>; also the transport MessageId.</summary>
    public Guid Id { get; set; }

    /// <summary>Stable wire name (the event's <c>[MessageUrn]</c>), resolved back to a CLR type on delivery.</summary>
    public string MessageType { get; set; } = default!;

    /// <summary>JSON of the concrete event (System.Text.Json, web defaults).</summary>
    public string Payload { get; set; } = default!;

    public DateTime OccurredOn { get; set; }

    /// <summary>The flow that raised the event ran in platform (host) scope; restored when publishing.</summary>
    public bool HostScope { get; set; }

    /// <summary>W3C trace id of the flow that raised the event (the publish span is parented on it).</summary>
    public string? TraceParent { get; set; }

    public int Attempts { get; set; }

    /// <summary>Not delivered before this instant (retry back-off).</summary>
    public DateTime NextAttemptAt { get; set; }

    /// <summary>Claim lease: a relay owns the row until then (multi-instance safe).</summary>
    public DateTime? LockedUntil { get; set; }

    public Guid? LockToken { get; set; }

    public DateTime? ProcessedAt { get; set; }

    /// <summary>Gave up after <see cref="OutboxOptions.MaxAttempts"/> (or undeliverable). Needs an operator.</summary>
    public DateTime? DeadLetteredAt { get; set; }

    public string? LastError { get; set; }
}

/// <summary>Outbox row of a HOST context (not an <see cref="ITenantDataContext"/>): the tenant is optional.</summary>
public sealed class OutboxMessage : OutboxMessageBase
{
    /// <summary>Ambient tenant of the flow that raised the event (<c>null</c> = none / platform).</summary>
    public Guid? TenantId { get; set; }
}

/// <summary>
/// Outbox row of a tenant-data context: tenant-owned like every entity there (named tenant filter, RLS, moved /
/// exported / purged with the tenant). <see cref="TenantId"/> is copied from the raising aggregate.
/// </summary>
public sealed class TenantOutboxMessage : OutboxMessageBase, IMustHaveTenant
{
    public Guid TenantId { get; set; }
}
