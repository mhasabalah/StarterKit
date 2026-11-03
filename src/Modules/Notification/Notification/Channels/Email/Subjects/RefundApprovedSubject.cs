namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="RefundApprovedModel"/>.</summary>
internal sealed class RefundApprovedSubject : IEmailTemplate
{
    public Type ModelType => typeof(RefundApprovedModel);

    public EmailTemplate Template => EmailTemplate.RefundApproved;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تمت الموافقة على طلب الاسترداد"
        : "Your Refund Has Been Approved";
}
