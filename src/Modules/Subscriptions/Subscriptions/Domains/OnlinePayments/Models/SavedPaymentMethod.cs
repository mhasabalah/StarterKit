namespace Subscriptions.Domains.OnlinePayments.Models;

/// <summary>
/// A payment method a subscriber saved at a provider while paying (a card, today). Only provider references and
/// display data are stored: never a card number, never a token that can be used elsewhere. Off-session dunning
/// charges the subscriber's DEFAULT active method; the subscriber (not support) manages them.
/// Exactly one active method per subscriber is the default (filtered unique index).
/// </summary>
public class SavedPaymentMethod
{
    public Guid Id { get; private set; }
    public Guid SubscriberId { get; private set; }

    /// <summary>The gateway adapter that owns the method (<c>Stripe</c>, ...).</summary>
    public string Gateway { get; private set; } = default!;

    /// <summary>The provider's customer id (Stripe <c>cus_...</c>).</summary>
    public string ProviderCustomerRef { get; private set; } = default!;

    /// <summary>The provider's payment method id (Stripe <c>pm_...</c>); unique per gateway.</summary>
    public string ProviderMethodRef { get; private set; } = default!;

    public string? Brand { get; private set; }
    public string? Last4 { get; private set; }
    public int? ExpMonth { get; private set; }
    public int? ExpYear { get; private set; }
    public bool IsDefault { get; private set; }

    /// <summary>When the subscriber agreed (at checkout) to keep this method for automatic renewal retries.</summary>
    public DateTimeOffset ConsentedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when the subscriber removed the method (or the provider reported it detached). Never reused for charges.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive => RevokedAt is null;

    private SavedPaymentMethod()
    {
    }

    public static SavedPaymentMethod Create(
        Guid subscriberId,
        string gateway,
        string providerCustomerRef,
        string providerMethodRef,
        string? brand,
        string? last4,
        int? expMonth,
        int? expYear,
        bool isDefault,
        DateTimeOffset now)
    {
        if (subscriberId == Guid.Empty)
            throw new ArgumentException("A saved method needs a subscriber.", nameof(subscriberId));
        if (string.IsNullOrWhiteSpace(gateway) || string.IsNullOrWhiteSpace(providerCustomerRef) ||
            string.IsNullOrWhiteSpace(providerMethodRef))
            throw new ArgumentException("Gateway, customer and method references are required.");

        return new SavedPaymentMethod
        {
            Id = Guid.NewGuid(),
            SubscriberId = subscriberId,
            Gateway = gateway,
            ProviderCustomerRef = providerCustomerRef,
            ProviderMethodRef = providerMethodRef,
            Brand = Trim(brand, 30),
            Last4 = Trim(last4, 4),
            ExpMonth = expMonth is >= 1 and <= 12 ? expMonth : null,
            ExpYear = expYear,
            IsDefault = isDefault,
            ConsentedAt = now,
            CreatedAt = now
        };
    }

    /// <summary>The card is past its last valid month (it is valid through the END of its expiry month).</summary>
    public bool IsExpired(DateTimeOffset now) =>
        ExpYear is { } year && ExpMonth is { } month &&
        new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1) <= now.UtcDateTime;

    /// <summary>The subscriber saved the same provider method again: it is usable again (details refreshed).</summary>
    public void Reactivate(string? brand, string? last4, int? expMonth, int? expYear, DateTimeOffset now)
    {
        RevokedAt = null;
        ConsentedAt = now; // saving it again is a fresh opt-in
        Brand = Trim(brand, 30) ?? Brand;
        Last4 = Trim(last4, 4) ?? Last4;
        ExpMonth = expMonth is >= 1 and <= 12 ? expMonth : ExpMonth;
        ExpYear = expYear ?? ExpYear;
    }

    public void SetDefault(bool isDefault)
    {
        if (isDefault && !IsActive)
            throw new ConflictException("A removed payment method cannot be the default.");
        IsDefault = isDefault;
    }

    /// <summary>Removes the method from use. A revoked method is never the default.</summary>
    public void Revoke(DateTimeOffset now)
    {
        RevokedAt ??= now;
        IsDefault = false;
    }

    private static string? Trim(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
