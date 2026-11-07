using Subscriptions.Contracts.Subscriptions.Enums;
using Shared.Contracts.Localization;

namespace Subscriptions.Domains.Plans.Models;

public class Plan : Aggregate<Guid>
{
    private readonly List<PlanFeature> _features = [];

    public LocalizedText Name { get; private set; } = default!;
    public string? SeedKey { get; private set; }
    public LocalizedText? Description { get; private set; }
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = default!;
    public int BillingCycleInMonths { get; private set; }
    public int? MaxItems { get; private set; }
    public int? MaxStorageGb { get; private set; }
    public int? MaxUsers { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsPublic { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public decimal? PendingPrice { get; private set; }
    public DateTimeOffset? PriceEffectiveAt { get; private set; }
    public DateTimeOffset? PriceChangeAnnouncedAt { get; private set; }
    public decimal? DiscountPercent { get; private set; }

    /// <summary>Free-trial length in days; 0 = the plan offers no trial.</summary>
    public int TrialDays { get; private set; }

    public const int MaxTrialDays = 365;
    public IReadOnlyList<PlanFeature> Features => _features.AsReadOnly();

    private Plan()
    {
    }

    public static Plan Create(
        LocalizedText name,
        LocalizedText? description,
        decimal price,
        string currency,
        int billingCycleInMonths,
        int? maxItems,
        int? maxStorageGb,
        int? maxUsers,
        bool isPublic,
        decimal? discountPercent = null,
        string? seedKey = null,
        int trialDays = 0)
    {
        EnsureValidTrialDays(trialDays);
        EnsureValidMoney(price, currency);
        return new Plan
        {
            Id = Guid.NewGuid(),
            Name = name,
            SeedKey = seedKey,
            Description = description,
            Price = price,
            Currency = currency,
            BillingCycleInMonths = billingCycleInMonths,
            MaxItems = maxItems,
            MaxStorageGb = maxStorageGb,
            MaxUsers = maxUsers,
            IsActive = true,
            IsPublic = isPublic,
            DiscountPercent = discountPercent,
            TrialDays = trialDays,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>A price needs a real ISO 4217 currency and no more decimals than that currency has (KWD 3, JPY 0).</summary>
    private static void EnsureValidMoney(decimal price, string currency)
    {
        if (!CurrencyRules.IsKnown(currency))
            throw new BadRequestException($"'{currency}' is not a valid ISO 4217 currency code.");

        if (!CurrencyRules.HasValidScale(price, currency))
            throw new BadRequestException(
                $"A {currency} price has at most {CurrencyRules.Exponent(currency)} decimals.");
    }

    private static void EnsureValidTrialDays(int trialDays)
    {
        if (trialDays is < 0 or > MaxTrialDays)
            throw new BadRequestException($"TrialDays must be between 0 and {MaxTrialDays}.");
    }

    public void Update(
        LocalizedText name,
        LocalizedText? description,
        string currency,
        int billingCycleInMonths,
        int? maxItems,
        int? maxStorageGb,
        int? maxUsers,
        bool isActive,
        bool isPublic,
        decimal? discountPercent = null,
        int trialDays = 0)
    {
        EnsureValidTrialDays(trialDays);
        // Only a currency CHANGE re-checks the stored prices: an unrelated edit of a legacy row (a price saved before
        // per-currency scales) must not start failing. See ADR 0017 for the data-fix query.
        if (!string.Equals(Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            EnsureValidMoney(Price, currency);
            if (PendingPrice is { } pending)
                EnsureValidMoney(pending, currency);
        }

        TrialDays = trialDays;
        Name = name;
        Description = description;
        Currency = currency;
        BillingCycleInMonths = billingCycleInMonths;
        MaxItems = maxItems;
        MaxStorageGb = maxStorageGb;
        MaxUsers = maxUsers;
        IsActive = isActive;
        IsPublic = isPublic;
        DiscountPercent = discountPercent;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ForcePrice(decimal newPrice)
    {
        EnsureValidMoney(newPrice, Currency);
        // Clears any pending scheduled change — immediate override wins.
        PendingPrice = null;
        PriceEffectiveAt = null;
        PriceChangeAnnouncedAt = null;
        Price = newPrice;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void EnsureSeedKey(string seedKey)
    {
        if (string.IsNullOrWhiteSpace(seedKey) || !string.IsNullOrWhiteSpace(SeedKey))
        {
            return;
        }

        SeedKey = seedKey;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // Updates the Arabic name without touching price/version — used only by PlanSeedService.
    public void SyncArabicName(string arabicName)
    {
        if (Name.Ar == arabicName)
            return;

        Name = LocalizedText.Create(Name.En, arabicName);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SchedulePriceChange(decimal newPrice, DateTimeOffset effectiveAt)
    {
        EnsureValidMoney(newPrice, Currency);
        PendingPrice = newPrice;
        PriceEffectiveAt = effectiveAt.ToUniversalTime(); // timestamptz: Npgsql only writes offset 0
        PriceChangeAnnouncedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyPriceChange()
    {
        if (PendingPrice is null || PriceEffectiveAt is null)
            return;

        Price = PendingPrice.Value;
        PendingPrice = null;
        PriceEffectiveAt = null;
        PriceChangeAnnouncedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void CancelPriceChange()
    {
        if (PendingPrice is null)
            throw new ConflictException("No scheduled price change to cancel.");

        PendingPrice = null;
        PriceEffectiveAt = null;
        PriceChangeAnnouncedAt = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public PlanFeature AddFeature(string key, long? limit = null, QuotaResetPeriod? resetPeriod = null)
    {
        var feature = PlanFeature.Create(Id, key, limit, resetPeriod);
        _features.Add(feature);
        UpdatedAt = DateTimeOffset.UtcNow;

        return feature;
    }

    public void RemoveFeature(string key)
    {
        var existing = _features.FirstOrDefault(f => f.Key == key);
        if (existing is null)
        {
            throw new PlanFeatureNotFoundException(key);
        }

        _features.Remove(existing);
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
