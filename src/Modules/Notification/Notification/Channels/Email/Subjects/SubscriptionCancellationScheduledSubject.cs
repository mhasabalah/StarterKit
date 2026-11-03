namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionCancellationScheduledModel"/>.</summary>
internal sealed class SubscriptionCancellationScheduledSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionCancellationScheduledModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionCancellationScheduled;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم جدولة إلغاء اشتراكك"
        : "Your Subscription Cancellation Has Been Scheduled";
}
