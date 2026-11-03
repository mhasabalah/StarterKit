namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionUpgradeRevertedModel"/>.</summary>
internal sealed class SubscriptionUpgradeRevertedSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionUpgradeRevertedModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionUpgradeReverted;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم إلغاء ترقية اشتراكك"
        : "Subscription Upgrade Reverted";
}
