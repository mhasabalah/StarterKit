namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="PriceChangeNotificationModel"/>.</summary>
internal sealed class PriceChangeNotificationSubject : IEmailTemplate
{
    public Type ModelType => typeof(PriceChangeNotificationModel);

    public EmailTemplate Template => EmailTemplate.PriceChangeNotification;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "إشعار بتغيير سعر اشتراكك"
        : "Upcoming Price Change for Your Subscription";
}
