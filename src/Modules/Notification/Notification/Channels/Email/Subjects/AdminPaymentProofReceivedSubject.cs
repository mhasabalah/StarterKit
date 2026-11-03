namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="AdminPaymentProofReceivedModel"/>.</summary>
internal sealed class AdminPaymentProofReceivedSubject : IEmailTemplate
{
    public Type ModelType => typeof(AdminPaymentProofReceivedModel);

    public EmailTemplate Template => EmailTemplate.AdminPaymentProofReceived;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "إثبات دفع جديد بانتظار المراجعة"
        : "New Payment Proof Awaiting Review";
}
