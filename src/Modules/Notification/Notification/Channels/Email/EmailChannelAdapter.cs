namespace Notification.Channels.Email;

/// <summary>
/// Email channel: renders the Liquid template and hands it to the configured transport. The delivery outcome is
/// recorded by <see cref="NotificationDispatchJob"/> in <c>notification_log</c> (the ONE delivery log of every channel).
/// </summary>
internal sealed class EmailChannelAdapter(
    IEmailService emailService,
    IEmailTemplateRenderer renderer,
    EmailTemplateRegistry registry,
    ILogger<EmailChannelAdapter> logger) : INotificationChannelAdapter
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public bool CanSend(string? email, string? phone)
        => !string.IsNullOrWhiteSpace(email);

    public async Task SendAsync(
        NotificationMessage message,
        string? email,
        string? phone,
        CancellationToken ct)
    {
        var payload = message.Email!;
        var subject = registry.ResolveSubject(payload.Template, message.Culture);
        var templateFileName = FluidTemplateRenderer.GetTemplateFileName(payload.Template);

        var html = await renderer.RenderAsync(templateFileName, payload.Model, message.Culture, ct);
        await emailService.SendAsync([email!], subject, html, attachments: payload.Attachments, ct: ct);

        logger.LogInformation(
            "Email notification sent. Template: {Template} Recipient: {Email}",
            payload.Template, PiiMask.Email(email));
    }
}
