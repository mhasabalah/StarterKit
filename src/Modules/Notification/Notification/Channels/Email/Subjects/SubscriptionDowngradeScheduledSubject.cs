namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionDowngradeScheduledModel"/>.</summary>
internal sealed class SubscriptionDowngradeScheduledSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionDowngradeScheduledModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionDowngradeScheduled;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم جدولة تخفيض اشتراكك"
        : "Downgrade Scheduled for Your Subscription";
}
