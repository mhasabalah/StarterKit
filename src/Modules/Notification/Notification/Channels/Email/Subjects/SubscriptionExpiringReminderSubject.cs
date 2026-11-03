namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionExpiringReminderModel"/>.</summary>
internal sealed class SubscriptionExpiringReminderSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionExpiringReminderModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionExpiringReminder;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "اشتراكك على وشك الانتهاء"
        : "Your Subscription Is Expiring Soon";
}
