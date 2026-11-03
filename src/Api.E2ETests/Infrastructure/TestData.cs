namespace Api.E2ETests.Infrastructure;

/// <summary>
/// Builders for valid request payloads. Values with uniqueness constraints (e.g. SKU)
/// are randomised so tests stay independent while sharing one database.
/// </summary>
public static class TestData
{
    public static string UniqueSku(string prefix = "E2E") =>
        $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant();

    /// <summary>A Catalog create/update body: bilingual name, enums as names (they serialize as strings).</summary>
    public static Dictionary<string, object?> ValidCreateProductPayload(
        string? sku = null, decimal price = 49.99m, string nameEn = "E2E Product") => new()
    {
        ["name"] = new { en = nameEn, ar = "منتج اختبار" },
        ["description"] = "Created by the e2e suite.",
        ["sku"] = sku ?? UniqueSku(),
        ["type"] = "Physical",
        ["price"] = price,
        ["compareAtPrice"] = null,
        ["currency"] = "USD",
    };
}
