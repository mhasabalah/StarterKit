namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="PasswordResetRequiredModel"/>.</summary>
internal sealed class PasswordResetRequiredSubject : IEmailTemplate
{
    public Type ModelType => typeof(PasswordResetRequiredModel);

    public EmailTemplate Template => EmailTemplate.PasswordResetRequired;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "مطلوب إعادة تعيين كلمة المرور"
        : "Password Reset Required";
}
