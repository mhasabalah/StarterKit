namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="RefundRejectedModel"/>.</summary>
internal sealed class RefundRejectedSubject : IEmailTemplate
{
    public Type ModelType => typeof(RefundRejectedModel);

    public EmailTemplate Template => EmailTemplate.RefundRejected;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم رفض طلب الاسترداد"
        : "Your Refund Request Has Been Rejected";
}
