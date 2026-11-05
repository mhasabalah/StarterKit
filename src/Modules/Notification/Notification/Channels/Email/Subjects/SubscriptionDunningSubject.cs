namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionDunningModel"/>.</summary>
internal sealed class SubscriptionDunningSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionDunningModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionDunning;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تذكير بالدفع: تجديد اشتراكك لا يزال معلقًا"
        : "Payment reminder: your subscription renewal is still due";
}
