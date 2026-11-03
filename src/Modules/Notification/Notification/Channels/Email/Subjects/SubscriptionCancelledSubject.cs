namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionCancelledModel"/>.</summary>
internal sealed class SubscriptionCancelledSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionCancelledModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionCancelled;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم إلغاء اشتراكك"
        : "Your Subscription Has Been Cancelled";
}
