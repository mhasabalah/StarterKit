namespace Notification.Channels.Email.Subjects;

/// <summary>Template identity + localized subject of <see cref="WebhookEndpointDisabledModel"/>.</summary>
internal sealed class WebhookEndpointDisabledSubject : IEmailTemplate
{
    public Type ModelType => typeof(WebhookEndpointDisabledModel);

    public EmailTemplate Template => EmailTemplate.WebhookEndpointDisabled;

    public string GetSubject(NotificationCulture culture) => culture == NotificationCulture.Ar
        ? "تم تعطيل نقطة نهاية Webhook بعد إخفاقات متكررة"
        : "A webhook endpoint was disabled after repeated failures";
}
