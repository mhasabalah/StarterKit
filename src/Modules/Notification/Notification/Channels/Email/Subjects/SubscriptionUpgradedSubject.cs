namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionUpgradedModel"/>.</summary>
internal sealed class SubscriptionUpgradedSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionUpgradedModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionUpgraded;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تمت ترقية اشتراكك بنجاح"
        : "Your Subscription Has Been Upgraded";
}
