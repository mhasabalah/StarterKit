namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="EmailConfirmationModel"/>.</summary>
internal sealed class EmailConfirmationSubject : IEmailTemplate
{
    public Type ModelType => typeof(EmailConfirmationModel);

    public EmailTemplate Template => EmailTemplate.EmailConfirmation;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تأكيد بريدك الإلكتروني"
        : "Confirm your email address";
}
