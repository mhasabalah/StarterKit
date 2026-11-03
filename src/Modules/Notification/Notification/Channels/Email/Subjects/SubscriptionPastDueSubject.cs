namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionPastDueModel"/>.</summary>
internal sealed class SubscriptionPastDueSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionPastDueModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionPastDue;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "اشتراكك متأخر — يُرجى التجديد خلال فترة السماح"
        : "Subscription Past Due — Renew Within Grace Period";
}
