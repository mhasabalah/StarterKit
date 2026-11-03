namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="SubscriptionHardLockedModel"/>.</summary>
internal sealed class SubscriptionHardLockedSubject : IEmailTemplate
{
    public Type ModelType => typeof(SubscriptionHardLockedModel);

    public EmailTemplate Template => EmailTemplate.SubscriptionHardLocked;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم تجميد حسابك"
        : "Your Account Has Been Locked";
}
