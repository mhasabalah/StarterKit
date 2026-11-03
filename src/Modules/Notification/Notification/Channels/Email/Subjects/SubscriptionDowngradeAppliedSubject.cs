namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionDowngradeAppliedModel"/>.</summary>
internal sealed class SubscriptionDowngradeAppliedSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionDowngradeAppliedModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionDowngradeApplied;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم تطبيق خطتك الجديدة"
        : "Plan Updated — Subscription Renewed";
}
