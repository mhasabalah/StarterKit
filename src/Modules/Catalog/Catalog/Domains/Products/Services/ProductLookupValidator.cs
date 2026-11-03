namespace Catalog.Domains.Products.Services;

/// <summary>
/// Validates the lookup references of a create/update request against the database: the category,
/// tags and attribute types must exist and be active, attribute types must apply to the product type,
/// and each attribute value must match its type's data type. One round-trip per lookup kind.
/// </summary>
public interface IProductLookupValidator
{
    Task EnsureValidAsync(
        ProductType productType,
        Guid? categoryId,
        IReadOnlyCollection<Guid> tagIds,
        IReadOnlyCollection<ProductAttributeInput> attributes,
        CancellationToken cancellationToken = default);
}

public class ProductLookupValidator(CatalogDbContext dbContext) : IProductLookupValidator
{
    public async Task EnsureValidAsync(
        ProductType productType,
        Guid? categoryId,
        IReadOnlyCollection<Guid> tagIds,
        IReadOnlyCollection<ProductAttributeInput> attributes,
        CancellationToken cancellationToken = default)
    {
        if (categoryId is { } id &&
            !await dbContext.Categories.AnyAsync(c => c.Id == id && c.IsActive, cancellationToken))
        {
            throw new InvalidProductException($"Category '{id}' does not exist or is inactive.");
        }

        if (tagIds.Count > 0)
        {
            var requested = tagIds.Distinct().ToList();
            var found = await dbContext.Tags
                .Where(t => requested.Contains(t.Id) && t.IsActive)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            var missing = requested.Except(found).ToList();
            if (missing.Count > 0)
                throw new InvalidProductException($"Unknown or inactive tag(s): {string.Join(", ", missing)}.");
        }

        if (attributes.Count > 0)
        {
            var requested = attributes.Select(a => a.AttributeTypeId).Distinct().ToList();
            var types = await dbContext.AttributeTypes
                .AsNoTracking()
                .Where(a => requested.Contains(a.Id) && a.IsActive)
                .ToDictionaryAsync(a => a.Id, cancellationToken);

            var errors = new List<string>();
            foreach (var input in attributes)
            {
                if (!types.TryGetValue(input.AttributeTypeId, out var type))
                    errors.Add($"Attribute type '{input.AttributeTypeId}' does not exist or is inactive.");
                else if (!type.AppliesTo(productType))
                    errors.Add($"Attribute '{type.Name.En}' does not apply to {productType} products.");
                else if (!type.IsValidValue(input.Value))
                    errors.Add($"'{input.Value}' is not a valid {type.DataType} value for '{type.Name.En}'.");
            }

            if (errors.Count > 0)
                throw new InvalidProductException(string.Join(" ", errors));
        }
    }
}
