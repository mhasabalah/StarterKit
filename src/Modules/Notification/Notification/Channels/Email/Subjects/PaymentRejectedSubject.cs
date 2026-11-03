namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="PaymentRejectedModel"/>.</summary>
internal sealed class PaymentRejectedSubject : IEmailTemplate
{
    public Type ModelType => typeof(PaymentRejectedModel);

    public EmailTemplate Template => EmailTemplate.PaymentRejected;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم رفض إثبات الدفع — يُرجى إعادة الرفع"
        : "Payment Proof Rejected — Please Resubmit";
}
