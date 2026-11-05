namespace Subscriptions.Domains.OnlinePayments.Models;

/// <summary>
/// Inbox row of a signature-verified provider webhook. <c>(Provider, EventId)</c> is unique, so a re-delivered event
/// is stored once and never reprocessed. The normalized fields let the retry job process the row without the original
/// request headers (the signature was already verified when the row was stored).
/// </summary>
public class InboundWebhookEvent
{
    public Guid Id { get; private set; }
    public string Provider { get; private set; } = default!;
    public string EventId { get; private set; } = default!;
    public string Type { get; private set; } = default!;
    public string? ProviderReference { get; private set; }
    public decimal? Amount { get; private set; }
    public string? Currency { get; private set; }

    /// <summary>The provider's refund id (refund events): the idempotency key of the refund, besides the event id.</summary>
    public string? ProviderRefundId { get; private set; }

    /// <summary>The provider's payment / charge / transaction id the event reports, if any.</summary>
    public string? PaymentReference { get; private set; }

    /// <summary>The raw JSON body as received (jsonb).</summary>
    public string Payload { get; private set; } = default!;

    public InboundWebhookStatus Status { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private InboundWebhookEvent()
    {
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Attempts++;
        Status = InboundWebhookStatus.Processed;
        ProcessedAt = now;
        LastAttemptAt = now;
        LastError = null;
    }

    /// <param name="permanent">Retrying cannot help (mismatch, unknown reference): the attempts are exhausted at once.</param>
    public void MarkFailed(string error, DateTimeOffset now, int maxAttempts, bool permanent)
    {
        Attempts = permanent ? Math.Max(Attempts + 1, maxAttempts) : Attempts + 1;
        Status = InboundWebhookStatus.Failed;
        LastAttemptAt = now;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}
