namespace Shared.Email.Jobs;

[Queue("email")]
[AutomaticRetry(Attempts = 3)]
public sealed class EmailJob(
    IEmailTemplateRenderer renderer,
    IEmailService emailService,
    EmailLogRepository repository,
    EmailTemplateRegistry registry,
    ILogger<EmailJob> logger)
{
    public async Task ExecuteAsync(EmailMessage message, Guid emailLogId)
    {
        try
        {
            var subject = registry.ResolveSubject(message.Template, message.Culture);
            var html = await renderer.RenderAsync(message.Template.ToString(), message.Model, message.Culture);
            await emailService.SendAsync(message.Recipients, subject, html, message.ReplyTo, message.Cc);
            await repository.UpdateStatusAsync(emailLogId, EmailDeliveryStatus.Sent, null, DateTime.UtcNow, null);

            logger.LogInformation(
                "Email sent. Template: {Template} Recipient: {Recipient}",
                message.Template,
                message.Recipients.FirstOrDefault() ?? "unknown");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Email failed. Template: {Template} Recipient: {Recipient} Error: {Error}",
                message.Template,
                message.Recipients.FirstOrDefault() ?? "unknown",
                ex.Message);

            await repository.UpdateStatusAsync(emailLogId, EmailDeliveryStatus.Failed, ex.Message, null, null);
            throw;
        }
    }
}
