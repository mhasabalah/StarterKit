using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Notification.Channels.Email;

public sealed class MailKitEmailService(IOptions<EmailOptions> options) : IEmailService
{
    public async Task SendAsync(
        string[] recipients,
        string subject,
        string htmlBody,
        string? replyTo = null,
        string[]? cc = null,
        EmailAttachment[]? attachments = null,
        CancellationToken ct = default)
    {
        var opts = options.Value;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(opts.FromName, opts.FromAddress));
        foreach (var recipient in recipients)
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        if (cc is { Length: > 0 })
        {
            foreach (var ccAddress in cc)
            {
                message.Cc.Add(MailboxAddress.Parse(ccAddress));
            }
        }

        if (!string.IsNullOrWhiteSpace(replyTo))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        }

        // C6: never let CR/LF reach the Subject header (header injection).
        message.Subject = EmailContentSafety.SanitizeSubject(subject);

        // multipart/alternative: a plain-text part (derived from the HTML) for text-only clients, screen readers
        // and better deliverability, then the HTML part (clients pick the LAST part they can render).
        var alternative = new MultipartAlternative
        {
            new TextPart(MimeKit.Text.TextFormat.Plain) { Text = EmailPlainText.FromHtml(htmlBody) },
            new TextPart(MimeKit.Text.TextFormat.Html) { Text = htmlBody }
        };

        if (attachments is { Length: > 0 })
        {
            var body = new Multipart("mixed") { alternative };
            foreach (var att in attachments)
            {
                var part = new MimePart(ContentType.Parse(att.ContentType))
                {
                    Content = new MimeContent(new MemoryStream(att.Content)),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    FileName = att.FileName
                };
                body.Add(part);
            }
            message.Body = body;
        }
        else
        {
            message.Body = alternative;
        }

        // TODO(OAUTH2): Replace AuthenticateAsync(user, pass) with
        // AuthenticateAsync(new SaslMechanismOAuth2(user, accessToken))
        // when Microsoft deprecates SMTP basic auth.
        // Acquire token via MSAL using client_credentials flow with
        // scope https://outlook.office365.com/.default.
        using var client = new SmtpClient();

        // Let the OS negotiate the TLS version (TLS 1.3 where available) instead of pinning TLS 1.2.
        client.SslProtocols = System.Security.Authentication.SslProtocols.None;

        await client.ConnectAsync(opts.SmtpHost, opts.SmtpPort, ResolveSocketOptions(opts), ct);

        if (opts.UseTls && !string.IsNullOrWhiteSpace(opts.SmtpUsername))
            await client.AuthenticateAsync(opts.SmtpUsername, opts.SmtpPassword, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }

    /// <summary>
    /// Port 465 is implicit TLS (SslOnConnect); every other port with UseTls upgrades via STARTTLS
    /// (and refuses to continue if the server does not offer it). UseTls=false is for local relays
    /// such as Mailhog only.
    /// </summary>
    public static SecureSocketOptions ResolveSocketOptions(EmailOptions opts)
        => !opts.UseTls
            ? SecureSocketOptions.None
            : opts.SmtpPort == 465
                ? SecureSocketOptions.SslOnConnect
                : SecureSocketOptions.StartTls;
}
