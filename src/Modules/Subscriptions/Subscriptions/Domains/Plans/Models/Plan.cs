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
