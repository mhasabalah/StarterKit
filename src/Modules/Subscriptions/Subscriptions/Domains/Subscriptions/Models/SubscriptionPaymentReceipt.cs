namespace Subscriptions.Domains.Subscriptions.Models;

/// <summary>
/// One amount received offline against a <see cref="SubscriptionPayment"/> (manual / partial payment), recorded by an
/// admin. Insert-only: a wrong receipt is corrected with a refund or a credit note, never edited.
/// </summary>
public class SubscriptionPaymentReceipt
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public string Method { get; private set; } = default!;
    public string? Reference { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string? Note { get; private set; }
    public Guid RecordedBy { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    private SubscriptionPaymentReceipt()
    {
    }

    internal static SubscriptionPaymentReceipt Create(
        Guid paymentId,
        Guid subscriptionId,
        decimal amount,
        string currency,
        string method,
        string? reference,
        DateTimeOffset receivedAt,
        string? note,
        Guid recordedBy,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        PaymentId = paymentId,
        SubscriptionId = subscriptionId,
        Amount = amount,
        Currency = currency,
        Method = method.Trim(),
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
        ReceivedAt = receivedAt.ToUniversalTime(), // timestamptz: Npgsql only writes offset 0
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        RecordedBy = recordedBy,
        RecordedAt = now
    };
}
