namespace Shared.Email.Persistence;

public sealed record FailedEmailDto(
    Guid Id,
    Guid? UserId,
    string TemplateName,
    string Recipient,
    string Subject,
    EmailCulture Culture,
    EmailDeliveryStatus Status,
    string? Error,
    DateTime CreatedAt,
    string? HangfireJobId);
