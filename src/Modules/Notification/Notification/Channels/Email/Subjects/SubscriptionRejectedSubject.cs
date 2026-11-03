namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionRejectedModel"/>.</summary>
internal sealed class SubscriptionRejectedSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionRejectedModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionRejected;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم رفض طلب الاشتراك"
        : "Subscription Request Rejected";
}
