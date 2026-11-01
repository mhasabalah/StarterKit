using Shared.Email.Contracts;
using Shared.Email.Enums;
using Shared.Email.Persistence;
using Shared.Email.Services;

namespace Shared.Notification.Channels.Email;

internal sealed class EmailChannelAdapter(
    IEmailService emailService,
    IEmailTemplateRenderer renderer,
    EmailTemplateRegistry registry,
    EmailLogRepository emailLogRepository,
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
        var culture = (EmailCulture)(int)message.Culture;
        var subject = registry.ResolveSubject(payload.Template, culture);
        var templateFileName = FluidTemplateRenderer.GetTemplateFileName(payload.Template);

        var html = await renderer.RenderAsync(templateFileName, payload.Model, culture, ct);
        await emailService.SendAsync([email!], subject, html, attachments: payload.Attachments, ct: ct);

        // Write to email_log so notification-path emails appear alongside direct-path emails.
        // Failures are covered by NotificationLog — we only record confirmed deliveries here.
        await emailLogRepository.InsertAsync(new EmailLog
        {
            Id = Guid.NewGuid(),
            UserId = message.UserId,
            TemplateName = payload.Template.ToString(),
            Recipient = email!,
            Subject = subject,
            Status = EmailDeliveryStatus.Sent,
            SentAt = DateTime.UtcNow,
            Culture = culture,
            CreatedAt = DateTime.UtcNow
        }, ct);

        logger.LogInformation(
            "Email notification sent. Template: {Template} Recipient: {Email}",
            payload.Template, email);
    }
}
