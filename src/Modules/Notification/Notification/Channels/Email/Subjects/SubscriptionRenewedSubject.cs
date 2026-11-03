namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionRenewedModel"/>.</summary>
internal sealed class SubscriptionRenewedSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionRenewedModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionRenewed;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم تجديد اشتراكك بنجاح"
        : "Your Subscription Has Been Renewed";
}
