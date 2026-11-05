namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="TrialEndingModel"/>.</summary>
internal sealed class TrialEndingSubject : IEmailTemplate
{
    public Type ModelType => typeof(TrialEndingModel);

    public EmailTemplate Template => EmailTemplate.TrialEnding;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "فترتك التجريبية على وشك الانتهاء"
        : "Your free trial is ending soon";
}
