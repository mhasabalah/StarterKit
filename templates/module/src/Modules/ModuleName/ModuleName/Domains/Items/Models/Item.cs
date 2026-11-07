using System.Text.RegularExpressions;
using ModuleName.Domains.Items.Events;
using ModuleName.Domains.Items.Exceptions;
#if (OUTBOX)
using ModuleName.Contracts.Items.IntegrationEvents;
#endif

namespace ModuleName.Domains.Items.Models;

/// <summary>
/// The example aggregate root: static factory, private setters, invariants in the aggregate, events raised here.
/// Replace it with your own (see the add-aggregate skill); keep the shape.
/// </summary>
#if (TENANT_DATA)
public partial class Item : Aggregate<Guid>, IMustHaveTenant
#else
public partial class Item : Aggregate<Guid>
#endif
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

#if (TENANT_DATA)
    /// <summary>Owning tenant: stamped on insert by TenantEntityInterceptor. Never set it.</summary>
    public Guid TenantId { get; set; }

#endif
    /// <summary>Natural key, unique, stored upper-case. Immutable after creation.</summary>
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public string? Description { get; private set; }

    private Item()
    {
    }

    public static Item Create(string code, string name, string? description)
    {
        var normalized = NormalizeCode(code);
        if (normalized.Length == 0 || normalized.Length > CodeMaxLength || !CodePattern().IsMatch(normalized))
            throw new InvalidItemException("code", code);

        var item = new Item { Id = Guid.NewGuid(), Code = normalized };
        item.SetDetails(name, description);

        item.AddDomainEvent(new ItemCreatedEvent(item.Id, item.Code));
#if (OUTBOX)
        item.AddIntegrationEvent(new ItemCreatedIntegrationEvent { ItemId = item.Id, Code = item.Code });
#endif
        return item;
    }

    public void Update(string name, string? description) => SetDetails(name, description);

    /// <summary>Codes compare case-insensitively: they are stored trimmed and upper-case.</summary>
    public static string NormalizeCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private void SetDetails(string name, string? description)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0 || trimmedName.Length > NameMaxLength)
            throw new InvalidItemException("name", name);

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is { Length: > DescriptionMaxLength })
            throw new InvalidItemException("description", description);

        Name = trimmedName;
        Description = trimmedDescription;
    }

    [GeneratedRegex("^[A-Z0-9_-]+$")]
    internal static partial Regex CodePattern();
}
