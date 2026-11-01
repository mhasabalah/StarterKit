namespace Subscriptions.Domains.PaymentSettings.Models;

public class PaymentMethod : Aggregate<Guid>
{
    public string Type { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string Value { get; private set; } = default!;
    public string? Description { get; private set; }
    public string? Metadata { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    private PaymentMethod() { }

    public static PaymentMethod Create(
        string type,
        string displayName,
        string value,
        string? description,
        string? metadata,
        int sortOrder) => new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            DisplayName = displayName,
            Value = value,
            Description = description,
            Metadata = metadata,
            SortOrder = sortOrder,
            IsActive = true
        };

    public void Update(
        string type,
        string displayName,
        string value,
        string? description,
        string? metadata,
        int sortOrder,
        bool isActive)
    {
        Type = type;
        DisplayName = displayName;
        Value = value;
        Description = description;
        Metadata = metadata;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}
