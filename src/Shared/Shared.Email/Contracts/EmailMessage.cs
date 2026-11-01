namespace Shared.Email.Contracts;

public sealed record EmailMessage(
    EmailTemplate Template,
    Dictionary<string, object> Model,
    string[] Recipients,
    EmailCulture Culture = EmailCulture.En,
    string? ReplyTo = null,
    string[]? Cc = null,
    Guid? UserId = null
);
