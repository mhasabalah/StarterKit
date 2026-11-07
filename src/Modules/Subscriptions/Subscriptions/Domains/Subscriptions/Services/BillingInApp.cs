using System.Resources;
using Notification.Contracts.InApp;
using Shared.Core.Localization;
using Subscriptions.Resources;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// The in-app inbox entry (and optional push) that accompanies a billing email. Category <c>billing.&lt;event&gt;</c>;
/// title and body come from <c>SubscriptionsResource</c> keys <c>InApp.&lt;event&gt;.Title</c> / <c>.Body</c> in BOTH
/// languages (the recipient's culture picks one at delivery). Amounts are deliberately left out of the text: the
/// inbox shows the event, the invoice or portal holds the figures.
/// </summary>
internal static class BillingInApp
{
    public const string CategoryGroup = "billing";

    private static readonly ResourceManager Resources = new(typeof(SubscriptionsResource));

    /// <summary>Builds the content. <paramref name="args"/> fill <c>{0}</c>, <c>{1}</c>... in the title and body.</summary>
    public static InAppContent Content(
        string eventKey, NotificationSeverity severity, string actionUrl, IReadOnlyDictionary<string, string>? data = null,
        params object?[] args) =>
        new(
            Title: BilingualResource.Get(Resources, $"InApp.{eventKey}.Title", args),
            Body: BilingualResource.Get(Resources, $"InApp.{eventKey}.Body", args),
            Category: $"{CategoryGroup}.{eventKey}",
            ActionUrl: actionUrl,
            Severity: severity,
            Data: data);

    /// <summary>Adds the in-app entry, and a push when the event needs the user's attention (<paramref name="push"/>).</summary>
    public static INotificationBuilder WithBillingInApp(
        this INotificationBuilder builder, string eventKey, NotificationSeverity severity, string actionUrl,
        bool push = false, bool sensitivePush = false, IReadOnlyDictionary<string, string>? data = null,
        params object?[] args)
    {
        builder.WithInApp(Content(eventKey, severity, actionUrl, data, args));
        return push ? builder.WithPush(sensitivePush) : builder;
    }

    public static IReadOnlyDictionary<string, string> SubscriptionData(Guid subscriptionId) =>
        new Dictionary<string, string> { ["subscriptionId"] = subscriptionId.ToString() };

    public const string SubscriptionUrl = "/subscription";
    public const string RenewUrl = "/subscription/renew";
}
