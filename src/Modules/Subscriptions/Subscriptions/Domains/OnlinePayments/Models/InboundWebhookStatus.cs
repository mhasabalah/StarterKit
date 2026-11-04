namespace Subscriptions.Domains.OnlinePayments.Models;

public enum InboundWebhookStatus
{
    /// <summary>Stored, not processed yet.</summary>
    Pending = 0,

    /// <summary>Handled (settled, or nothing to do).</summary>
    Processed = 1,

    /// <summary>Processing failed; retried by the job until the attempts run out or the failure is permanent.</summary>
    Failed = 2
}
