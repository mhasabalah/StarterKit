using System.Text.Json.Serialization;

namespace Catalog.Contracts.Products.Enums;

/// <summary>
/// Moderation workflow. Allowed transitions (enforced by the Product aggregate):
/// Draft/ChangesRequested/Rejected → PendingReview → Approved | Rejected | ChangesRequested;
/// Approved → Published → Archived → Draft.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductStatus
{
    [LocalizedDescription("Draft", "مسودة")]
    Draft = 1,

    [LocalizedDescription("Pending Review", "قيد المراجعة")]
    PendingReview = 2,

    [LocalizedDescription("Changes Requested", "مطلوب تعديلات")]
    ChangesRequested = 3,

    [LocalizedDescription("Approved", "معتمد")]
    Approved = 4,

    [LocalizedDescription("Published", "منشور")]
    Published = 5,

    [LocalizedDescription("Rejected", "مرفوض")]
    Rejected = 6,

    [LocalizedDescription("Archived", "مؤرشف")]
    Archived = 7
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductType
{
    [LocalizedDescription("Physical", "منتج مادي")]
    Physical = 1,

    [LocalizedDescription("Digital", "منتج رقمي")]
    Digital = 2,

    [LocalizedDescription("Service", "خدمة")]
    Service = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Currency
{
    [LocalizedDescription("US Dollar", "دولار أمريكي")]
    USD = 1,

    [LocalizedDescription("Euro", "يورو")]
    EUR = 2,

    [LocalizedDescription("UAE Dirham", "درهم إماراتي")]
    AED = 3,

    [LocalizedDescription("Saudi Riyal", "ريال سعودي")]
    SAR = 4,

    [LocalizedDescription("Egyptian Pound", "جنيه مصري")]
    EGP = 5
}

/// <summary>How the products of a search matched the text.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductSearchMatch
{
    /// <summary>No text: filters only.</summary>
    [LocalizedDescription("Browse", "تصفح")]
    Browse = 0,

    /// <summary>The text appears as typed in the name, SKU or description (SQL search, no search index).</summary>
    [LocalizedDescription("Contains", "يحتوي على")]
    Contains = 1,

    /// <summary>The words matched (search index: stemming and Arabic normalization included).</summary>
    [LocalizedDescription("Exact match", "مطابقة تامة")]
    Exact = 2,

    /// <summary>Nothing matched exactly; these products have close spellings.</summary>
    [LocalizedDescription("Close spelling", "تهجئة قريبة")]
    Fuzzy = 3
}

/// <summary>The value counts a product search can return (search index only).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProductFacet
{
    [LocalizedDescription("Category", "الفئة")]
    Category = 1,

    [LocalizedDescription("Tag", "الوسم")]
    Tag = 2,

    [LocalizedDescription("Type", "النوع")]
    Type = 3,

    [LocalizedDescription("Currency", "العملة")]
    Currency = 4,

    [LocalizedDescription("Featured", "مميز")]
    Featured = 5
}
