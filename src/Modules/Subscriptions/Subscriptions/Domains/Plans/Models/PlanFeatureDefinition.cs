using Shared.Contracts.Localization;

namespace Subscriptions.Domains.Plans.Models;

public class PlanFeatureDefinition
{
    public string Key { get; private set; } = default!;
    public LocalizedText DisplayName { get; private set; } = default!;
    public LocalizedText? Description { get; private set; }
    public string? Category { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PlanFeatureDefinition() { }

    public static PlanFeatureDefinition Create(
        string key,
        LocalizedText displayName,
        LocalizedText? description = null,
        string? category = null,
        int sortOrder = 0)
    {
        return new PlanFeatureDefinition
        {
            Key = key,
            DisplayName = displayName,
            Description = description,
            Category = category,
            SortOrder = sortOrder,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Update(LocalizedText displayName, LocalizedText? description, string? category, int sortOrder)
    {
        DisplayName = displayName;
        Description = description;
        Category = category;
        SortOrder = sortOrder;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
