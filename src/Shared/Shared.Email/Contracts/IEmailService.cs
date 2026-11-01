namespace Shared.Email.Contracts;

public interface IEmailService
{
    Task SendAsync(
        string[] recipients,
        string subject,
        string htmlBody,
        string? replyTo = null,
        string[]? cc = null,
        EmailAttachment[]? attachments = null,
        CancellationToken ct = default);
}
