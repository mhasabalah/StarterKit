namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="PriceChangeCancelledNotificationModel"/>.</summary>
internal sealed class PriceChangeCancelledNotificationSubject : IEmailTemplate
{
    public Type ModelType => typeof(PriceChangeCancelledNotificationModel);

    public EmailTemplate Template => EmailTemplate.PriceChangeCancelled;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم إلغاء تغيير سعر اشتراكك"
        : "Scheduled Price Change Cancelled";
}
