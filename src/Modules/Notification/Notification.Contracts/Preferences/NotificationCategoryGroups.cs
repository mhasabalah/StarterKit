namespace Notification.Contracts.Preferences;

/// <summary>
/// The notification category groups a user can mute per channel. A category is <c>&lt;group&gt;.&lt;event&gt;</c>
/// (e.g. <c>billing.invoice-issued</c>); the group is the part before the first dot. A category with no dot, or an
/// unknown group, belongs to <see cref="System"/>. <see cref="Security"/> can never be muted.
/// </summary>
public static class NotificationCategoryGroups
{
    public const string Billing = "billing";
    public const string Security = "security";
    public const string Membership = "membership";
    public const string Catalog = "catalog";
    public const string DataTransfer = "data-transfer";
    public const string Webhooks = "webhooks";
    public const string System = "system";

    /// <summary>Every group with its bilingual display name, in presentation order.</summary>
    public static readonly IReadOnlyList<NotificationCategoryGroup> All =
    [
        new(Billing, new LocalizedText("Billing and subscriptions", "الفوترة والاشتراكات"), true),
        new(Security, new LocalizedText("Security", "الأمان"), false),
        new(Membership, new LocalizedText("Team and membership", "الفريق والعضوية"), true),
        new(Catalog, new LocalizedText("Catalog", "الكتالوج"), true),
        new(DataTransfer, new LocalizedText("Imports and exports", "الاستيراد والتصدير"), true),
        new(Webhooks, new LocalizedText("Webhooks", "الويب هوكس"), true),
        new(System, new LocalizedText("System", "النظام"), true),
    ];

    public static bool IsKnown(string? group) =>
        group is not null && All.Any(g => string.Equals(g.Key, group, StringComparison.OrdinalIgnoreCase));

    /// <summary>The group of <paramref name="category"/>; <see cref="System"/> when it has no dot or an unknown group.</summary>
    public static string GroupOf(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return System;

        var dot = category.IndexOf('.', StringComparison.Ordinal);
        var prefix = (dot < 0 ? string.Empty : category[..dot]).Trim().ToLowerInvariant();
        return IsKnown(prefix) ? prefix : System;
    }

    /// <summary>False for <see cref="Security"/>: it is always delivered on every channel.</summary>
    public static bool IsMutable(string group) =>
        !string.Equals(group, Security, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A mutable (or fixed) category group as served by the lookup endpoint.</summary>
public sealed record NotificationCategoryGroup(string Key, LocalizedText Name, bool Mutable);
