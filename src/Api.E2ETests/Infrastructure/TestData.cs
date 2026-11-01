namespace Api.E2ETests.Infrastructure;

/// <summary>
/// Builders for valid request payloads. Values with uniqueness constraints (e.g. SKU)
/// are randomised so tests stay independent while sharing one database.
/// </summary>
public static class TestData
{
    public static string UniqueSku(string prefix = "E2E") =>
        $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant();

    public static object ValidCreateProductPayload(string? sku = null, decimal price = 49.99m) => new
    {
        Name = "E2E Product",
        Description = "Created by the e2e suite.",
        Sku = sku ?? UniqueSku(),
        Price = price,
        IsActive = true,
        ImageAttachmentId = (Guid?)null,
    };
}
