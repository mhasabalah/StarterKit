using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Shared.Email.Options;

namespace Shared.Email.Services;

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

        message.Subject = subject;

        if (attachments is { Length: > 0 })
        {
            var body = new Multipart("mixed");
            body.Add(new TextPart(MimeKit.Text.TextFormat.Html) { Text = htmlBody });
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
            message.Body = new TextPart(MimeKit.Text.TextFormat.Html) { Text = htmlBody };
        }

        // TODO(OAUTH2): Replace AuthenticateAsync(user, pass) with
        // AuthenticateAsync(new SaslMechanismOAuth2(user, accessToken))
        // when Microsoft deprecates SMTP basic auth.
        // Acquire token via MSAL using client_credentials flow with
        // scope https://outlook.office365.com/.default.
        using var client = new SmtpClient();
        if (opts.UseTls)
        {
            client.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            await client.ConnectAsync(opts.SmtpHost, opts.SmtpPort, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(opts.SmtpUsername, opts.SmtpPassword, ct);
        }
        else
        {
            await client.ConnectAsync(opts.SmtpHost, opts.SmtpPort, SecureSocketOptions.None, ct);
        }
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
