namespace Notification.Channels.Email;

public interface IEmailTemplateRenderer
{
    Task<string> RenderAsync(
        string templateName,
        Dictionary<string, object> model,
        NotificationCulture culture,
        CancellationToken ct = default);
}
