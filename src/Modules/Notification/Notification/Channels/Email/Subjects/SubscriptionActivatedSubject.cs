namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionActivatedModel"/>.</summary>
internal sealed class SubscriptionActivatedSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionActivatedModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionActivated;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم تفعيل اشتراكك بنجاح"
        : "Your Subscription Is Now Active";
}
