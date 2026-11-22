namespace Notification.Contracts.Preferences;

/// <summary>
/// The notification category groups a user can mute per channel. A category is <c>&lt;group&gt;.&lt;event&gt;</c>
/// (e.g. <c>billing.invoice-issued</c>); the group is the part before the first dot. A category with no dot, or an
/// unknown group, belongs to <see cref="System"/>.
/// <para>
/// <b>Fixed channels</b> (the user cannot mute them; the validator rejects it and delivery ignores a stale row):
/// <c>security</c> on every channel, and <c>billing</c> email (transactional: invoices, payment failures).
/// <c>security</c> is also never held back by quiet hours.
/// </para>
/// </summary>
public static class NotificationCategoryGroups
{
    public const string Billing = "billing";
    public const string Security = "security";
    public const string Membership = "membership";
    public const string Catalog = "catalog";
    public const string DataTransfer = "data-transfer";
    public const string Webhooks = "webhooks";
#if (AGENTIC)
    public const string Agents = "agents";
#endif
    public const string System = "system";

    /// <summary>Every group with its bilingual display name and which channels a user may mute, in presentation order.</summary>
    public static readonly IReadOnlyList<NotificationCategoryGroup> All =
    [
        new(Billing, new LocalizedText("Billing and subscriptions", "الفوترة والاشتراكات"), InAppMutable: true, PushMutable: true, EmailMutable: false),
        new(Security, new LocalizedText("Security", "الأمان"), false, false, false),
        new(Membership, new LocalizedText("Team and membership", "الفريق والعضوية"), true, true, true),
        new(Catalog, new LocalizedText("Catalog", "الكتالوج"), true, true, true),
        new(DataTransfer, new LocalizedText("Imports and exports", "الاستيراد والتصدير"), true, true, true),
        new(Webhooks, new LocalizedText("Webhooks", "الويب هوكس"), true, true, true),
#if (AGENTIC)
        new(Agents, new LocalizedText("AI agents", "وكلاء الذكاء الاصطناعي"), true, true, true),
#endif
        new(System, new LocalizedText("System", "النظام"), true, true, true),
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

    /// <summary>
    /// True when the user may mute <paramref name="channel"/> for <paramref name="group"/>. Only in-app, push and email
    /// can be muted at all (email covers the whole email/WhatsApp chain).
    /// </summary>
    public static bool IsChannelMutable(string group, NotificationChannel channel)
    {
        var found = All.FirstOrDefault(g => string.Equals(g.Key, group, StringComparison.OrdinalIgnoreCase));
        return found is not null && channel switch
        {
            NotificationChannel.InApp => found.InAppMutable,
            NotificationChannel.Push => found.PushMutable,
            NotificationChannel.Email => found.EmailMutable,
            _ => false
        };
    }

    /// <summary>False for <see cref="Security"/>: it is delivered immediately, even during quiet hours.</summary>
    public static bool DefersForQuietHours(string group) =>
        !string.Equals(group, Security, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A category group as served by the lookup endpoint, with the channels the user may mute.</summary>
public sealed record NotificationCategoryGroup(string Key, LocalizedText Name, bool InAppMutable, bool PushMutable, bool EmailMutable);
