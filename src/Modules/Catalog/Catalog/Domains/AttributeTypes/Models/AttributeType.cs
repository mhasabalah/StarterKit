using Shared.Core.MultiTenancy;
using System.Globalization;

namespace Catalog.Domains.AttributeTypes.Models;

/// <summary>
/// Bilingual lookup describing a product attribute (Color, Weight, Waterproof, ...). The
/// <see cref="DataType"/> decides which values are valid; <see cref="ApplicableTypes"/> limits it to some
/// product types (null = all). Stored as <c>integer[]</c> so EF/Npgsql translate <c>Contains</c> to SQL.
/// </summary>
public class AttributeType : Entity<Guid>, IMustHaveTenant
{
    /// <summary>Owning tenant (stamped on insert by TenantEntityInterceptor; immutable).</summary>
    public Guid TenantId { get; set; }

    public const int NameMaxLength = 100;
    public const int UnitMaxLength = 20;

    public LocalizedText Name { get; private set; } = default!;
    public AttributeDataType DataType { get; private set; }
    public string? Unit { get; private set; }
    public int[]? ApplicableTypes { get; private set; }
    public bool IsActive { get; private set; }

    private AttributeType()
    {
    }

    public static AttributeType Create(
        LocalizedText name,
        AttributeDataType dataType,
        string? unit = null,
        IEnumerable<ProductType>? applicableTypes = null,
        Guid? id = null)
    {
        var attributeType = new AttributeType { Id = id ?? Guid.NewGuid(), IsActive = true };
        attributeType.Update(name, dataType, unit, applicableTypes);
        return attributeType;
    }

    public void Update(
        LocalizedText name,
        AttributeDataType dataType,
        string? unit,
        IEnumerable<ProductType>? applicableTypes)
    {
        if (name is null || string.IsNullOrWhiteSpace(name.En) || string.IsNullOrWhiteSpace(name.Ar))
            throw new BadRequestException("Attribute name is required in English and Arabic.");
        if (name.En.Trim().Length > NameMaxLength || name.Ar.Trim().Length > NameMaxLength)
            throw new BadRequestException($"Attribute name must not exceed {NameMaxLength} characters.");
        if (!Enum.IsDefined(dataType))
            throw new BadRequestException($"Attribute data type '{dataType}' is not valid.");
        if (unit is not null && unit.Trim().Length > UnitMaxLength)
            throw new BadRequestException($"Unit must not exceed {UnitMaxLength} characters.");

        var types = applicableTypes?.Distinct().ToArray();
        if (types is not null && types.Any(t => !Enum.IsDefined(t)))
            throw new BadRequestException("Applicable types contain an unknown product type.");

        Name = LocalizedText.Create(name.En.Trim(), name.Ar.Trim());
        DataType = dataType;
        Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        ApplicableTypes = types is null || types.Length == 0 ? null : types.Select(t => (int)t).Order().ToArray();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public bool AppliesTo(ProductType productType) =>
        ApplicableTypes is null || ApplicableTypes.Contains((int)productType);

    public bool IsValidValue(string? value) => IsValidValue(DataType, value);

    /// <summary>Text: non-empty. Number: invariant-culture decimal ("." separator, no grouping). Boolean: "true"/"false" (any case).</summary>
    public static bool IsValidValue(AttributeDataType dataType, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        return dataType switch
        {
            AttributeDataType.Text => true,
            // No thousands separators: "12,5" must not silently become 125.
            AttributeDataType.Number => decimal.TryParse(
                value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out _),
            AttributeDataType.Boolean => bool.TryParse(value.Trim(), out _),
            _ => false
        };
    }
}
