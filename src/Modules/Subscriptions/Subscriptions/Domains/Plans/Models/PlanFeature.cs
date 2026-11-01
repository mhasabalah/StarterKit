namespace Subscriptions.Domains.Plans.Models;

public class PlanFeature
{
    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public string Key { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    private PlanFeature() { }

    public static PlanFeature Create(Guid planId, string key)
    {
        return new PlanFeature
        {
            Id = Guid.NewGuid(),
            PlanId = planId,
            Key = key,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
