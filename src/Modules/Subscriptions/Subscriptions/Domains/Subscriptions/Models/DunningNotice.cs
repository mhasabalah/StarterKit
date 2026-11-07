namespace Subscriptions.Domains.Subscriptions.Models;

/// <summary>
/// One dunning step handled for one overdue renewal payment. The (payment, step) unique index is what makes the job
/// idempotent: a step never re-sends, even when two job runs overlap.
/// </summary>
public class DunningNotice
{
    /// <summary>Step key of the last reminder before the grace period ends.</summary>
    public const string FinalStep = "final";

    public static string StepKey(int day) => $"day-{day}";

    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public Guid PaymentId { get; private set; }

    /// <summary><c>day-N</c> (N days after past due) or <see cref="FinalStep"/>.</summary>
    public string Step { get; private set; } = default!;

    /// <summary>False when the step was due but superseded by a later one in the same run (never sent).</summary>
    public bool Sent { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>
    /// What the automatic card retry of this step did, when one was tried: <c>Succeeded</c>, <c>RequiresAction</c>,
    /// <c>Declined</c> or <c>Transient</c>. Null = no retry was attempted (disabled, no usable saved method).
    /// </summary>
    public string? AutoRetryOutcome { get; private set; }

    /// <summary>Records the retry result; a collected payment means no reminder went out, so the notice is not "sent".</summary>
    public void RecordAutoRetry(string outcome, bool reminderSent)
    {
        AutoRetryOutcome = outcome;
        Sent = reminderSent;
    }

    private DunningNotice()
    {
    }

    public static DunningNotice Record(Guid subscriptionId, Guid paymentId, string step, bool sent, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            PaymentId = paymentId,
            Step = step,
            Sent = sent,
            RecordedAt = now
        };
}
