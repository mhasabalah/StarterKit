namespace Shared.Email.Services;

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
            "[EMAIL SUPPRESSED - non-production] Recipients: {Recipients} Subject: {Subject} HtmlLength: {HtmlLength} ReplyTo: {ReplyTo} Cc: {Cc}",
            string.Join(", ", recipients),
            subject,
            htmlBody.Length,
            replyTo ?? "none",
            cc is { Length: > 0 } ? string.Join(", ", cc) : "none");

        return Task.CompletedTask;
    }
}
