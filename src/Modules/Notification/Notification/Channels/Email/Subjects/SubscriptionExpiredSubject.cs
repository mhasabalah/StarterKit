namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionExpiredModel"/>.</summary>
internal sealed class SubscriptionExpiredSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionExpiredModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionExpired;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "انتهت صلاحية اشتراكك"
        : "Your Subscription Has Expired";
}
