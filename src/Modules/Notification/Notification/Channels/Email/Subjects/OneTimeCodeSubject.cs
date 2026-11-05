namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="OneTimeCodeModel"/>. Never puts the code in the subject.</summary>
internal sealed class OneTimeCodeSubject : IEmailTemplate
{
    public Type ModelType => typeof(OneTimeCodeModel);

    public EmailTemplate Template => EmailTemplate.OneTimeCode;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "رمز التحقق الخاص بك"
        : "Your verification code";
}
