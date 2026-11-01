namespace Shared.Email.Contracts;

public interface IEmailJobEnqueuer
{
    Task<string> EnqueueAsync(EmailMessage message, CancellationToken ct = default);
    Task<string> ReEnqueueAsync(Guid emailLogId, CancellationToken ct = default);
}
