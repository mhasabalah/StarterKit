namespace Notification.Channels.Email;

/// <summary>
/// Development transport (Email:Provider = NoOp, or Auto in Development without a real SMTP host):
/// logs a masked summary instead of sending, so a dev box never mails real users.
/// </summary>
public sealed class NoOpEmailService(ILogger<NoOpEmailService> logger) : IEmailService
{
    public Task SendAsync(
        string[] recipients,
        string subject,
        string htmlBody,
        string? replyTo = null,
        string[]? cc = null,
        EmailAttachment[]? attachments = null,
        CancellationToken ct = default)
    {
        logger.LogInformation(
            "[EMAIL SUPPRESSED - NoOp provider] Recipients: {Recipients} Subject: {Subject} HtmlLength: {HtmlLength} ReplyTo: {ReplyTo} Cc: {Cc} Attachments: {AttachmentCount}",
            PiiMask.Emails(recipients),
            EmailContentSafety.SanitizeSubject(subject),
            htmlBody.Length,
            PiiMask.Email(replyTo),
            PiiMask.Emails(cc),
            attachments?.Length ?? 0);

        return Task.CompletedTask;
    }
}
