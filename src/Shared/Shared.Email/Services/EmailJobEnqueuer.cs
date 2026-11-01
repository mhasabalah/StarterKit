namespace Shared.Email.Services;

public sealed class EmailJobEnqueuer(
    EmailLogRepository repository,
    IBackgroundJobClient jobClient,
    EmailTemplateRegistry registry) : IEmailJobEnqueuer
{
    public async Task<string> EnqueueAsync(EmailMessage message, CancellationToken ct = default)
    {
        // P2: generate ID and call Enqueue (synchronous) before any DB write so the
        // HangfireJobId is available on the first — and only — InsertAsync call.
        var logId = Guid.NewGuid();
        var jobId = jobClient.Enqueue<EmailJob>(job => job.ExecuteAsync(message, logId));

        var log = new EmailLog
        {
            Id = logId,
            UserId = message.UserId,
            HangfireJobId = jobId,
            TemplateName = message.Template.ToString(),
            Recipient = message.Recipients.FirstOrDefault() ?? string.Empty,
            Subject = registry.ResolveSubject(message.Template, message.Culture),
            Status = EmailDeliveryStatus.Queued,
            Culture = message.Culture,
            CreatedAt = DateTime.UtcNow,
            MessageJson = JsonSerializer.Serialize(message)
        };

        await repository.InsertAsync(log, ct);
        return jobId;
    }

    public async Task<string> ReEnqueueAsync(Guid emailLogId, CancellationToken ct = default)
    {
        var log = await repository.GetByIdAsync(emailLogId, ct)
                  ?? throw new NotFoundException("EmailLog", emailLogId);

        if (log.Status != EmailDeliveryStatus.Failed)
            throw new InvalidOperationException(
                $"Cannot re-enqueue email log '{emailLogId}': current status is '{log.Status}'. Only failed records can be re-enqueued.");

        if (string.IsNullOrWhiteSpace(log.MessageJson))
            throw new InvalidOperationException($"Email log '{emailLogId}' has no stored message — cannot re-enqueue.");

        var message = JsonSerializer.Deserialize<EmailMessage>(log.MessageJson)
                      ?? throw new InvalidOperationException($"Stored message_json for '{emailLogId}' is invalid.");

        var newJobId = jobClient.Enqueue<EmailJob>(job => job.ExecuteAsync(message, emailLogId));
        await repository.UpdateStatusAsync(emailLogId, EmailDeliveryStatus.Queued, null, null, newJobId, ct);
        return newJobId;
    }
}
