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
