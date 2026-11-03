namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionDowngradeCancelledModel"/>.</summary>
internal sealed class SubscriptionDowngradeCancelledSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionDowngradeCancelledModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionDowngradeCancelled;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم إلغاء جدولة التخفيض"
        : "Scheduled Downgrade Cancelled";
}
