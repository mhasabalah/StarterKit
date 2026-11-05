namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SecurityAlertModel"/>.</summary>
internal sealed class SecurityAlertSubject : IEmailTemplate
{
    public Type ModelType => typeof(SecurityAlertModel);

    public EmailTemplate Template => EmailTemplate.SecurityAlert;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تنبيه أمني: تغيير في حسابك"
        : "Security alert: your account sign-in methods changed";
}
