namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="AdminBillingDigestModel"/>.</summary>
internal sealed class AdminBillingDigestSubject : IEmailTemplate
{
    public Type ModelType => typeof(AdminBillingDigestModel);

    public EmailTemplate Template => EmailTemplate.AdminBillingDigest;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "ملخص الفوترة اليومي"
        : "Daily Billing Digest";
}
