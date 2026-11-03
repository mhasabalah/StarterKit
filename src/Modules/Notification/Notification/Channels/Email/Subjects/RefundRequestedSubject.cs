namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="RefundRequestedModel"/>.</summary>
internal sealed class RefundRequestedSubject : IEmailTemplate
{
    public Type ModelType => typeof(RefundRequestedModel);

    public EmailTemplate Template => EmailTemplate.RefundRequested;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "طلب استرداد جديد يحتاج مراجعة"
        : "New Refund Request Awaiting Review";
}
