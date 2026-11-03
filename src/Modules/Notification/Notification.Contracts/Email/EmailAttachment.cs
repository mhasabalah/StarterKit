namespace Notification.Contracts.Email;

public sealed record EmailAttachment(
    string FileName,
    byte[] Content,
    string ContentType = "application/pdf");
