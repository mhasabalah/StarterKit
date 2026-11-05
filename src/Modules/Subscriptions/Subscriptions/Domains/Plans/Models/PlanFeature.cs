using Subscriptions.Contracts.Subscriptions.Enums;

namespace Subscriptions.Domains.Plans.Models;

/// <summary>
/// A feature a plan includes. A plain feature is on/off. A METERED feature also has a <see cref="Limit"/> (the quota per
/// <see cref="ResetPeriod"/>); usage is counted by the metering service and enforced by <c>.RequireQuota(key)</c>.
/// </summary>
public class PlanFeature
{
    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public string Key { get; private set; } = default!;

    /// <summary>Quota per reset period; null = not metered (or unlimited).</summary>
    public long? Limit { get; private set; }

    /// <summary>Set whenever <see cref="Limit"/> is set.</summary>
    public QuotaResetPeriod? ResetPeriod { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private PlanFeature() { }

    public static PlanFeature Create(Guid planId, string key, long? limit = null, QuotaResetPeriod? resetPeriod = null)
    {
        if (limit is < 0)
            throw new BadRequestException("A feature limit cannot be negative.");

        return new PlanFeature
        {
            Id = Guid.NewGuid(),
            PlanId = planId,
            Key = key,
            Limit = limit,
            ResetPeriod = limit is null ? null : resetPeriod ?? QuotaResetPeriod.Monthly,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
