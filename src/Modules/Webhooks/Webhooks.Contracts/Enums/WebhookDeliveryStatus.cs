using Shared.Contracts.Attributes;

namespace Webhooks.Contracts.Enums;

/// <summary>Lifecycle of one webhook delivery (a message to one endpoint, with all its attempts).</summary>
public enum WebhookDeliveryStatus
{
    /// <summary>Waiting for its first or next attempt.</summary>
    [LocalizedDescription("Pending", "قيد الانتظار")]
    Pending = 0,

    /// <summary>The endpoint answered 2xx.</summary>
    [LocalizedDescription("Succeeded", "تم بنجاح")]
    Succeeded = 1,

    /// <summary>Out of attempts, or failed permanently (4xx, blocked address). Can be redelivered manually.</summary>
    [LocalizedDescription("Failed", "فشل")]
    Failed = 2,

    /// <summary>Dropped without trying (endpoint disabled or deleted).</summary>
    [LocalizedDescription("Cancelled", "ملغى")]
    Cancelled = 3
}
