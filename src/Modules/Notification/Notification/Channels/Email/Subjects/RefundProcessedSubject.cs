namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="RefundProcessedModel"/>.</summary>
internal sealed class RefundProcessedSubject : IEmailTemplate
{
    public Type ModelType => typeof(RefundProcessedModel);

    public EmailTemplate Template => EmailTemplate.RefundProcessed;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم صرف مبلغ الاسترداد"
        : "Your Refund Has Been Processed";
}
