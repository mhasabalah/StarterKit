namespace Shared.Email.Contracts;

public sealed record EmailAttachment(
    string FileName,
    byte[] Content,
    string ContentType = "application/pdf");
