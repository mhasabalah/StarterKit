namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SsoCertificateExpiringModel"/>.</summary>
internal sealed class SsoCertificateExpiringSubject : IEmailTemplate
{
    public Type ModelType => typeof(SsoCertificateExpiringModel);

    public EmailTemplate Template => EmailTemplate.SsoCertificateExpiring;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "شهادة تسجيل الدخول الموحد (SSO) توشك على الانتهاء"
        : "An SSO signing certificate is about to expire";
}
