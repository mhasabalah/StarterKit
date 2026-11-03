namespace Identity.Contracts.Auth.Commands;

/// <summary>
/// (Re)sends the confirmation link to the signed-in user's email.
/// <see cref="UserId"/> is always the authenticated caller (set by the endpoint from claims, never from the body).
/// </summary>
public record SendEmailConfirmationCommand(Guid UserId) : ICommand<SendEmailConfirmationResult>;

public record SendEmailConfirmationResult(string Message);

/// <summary>Confirms an email with the userId + token from the confirmation link. Anonymous (the link is the proof).</summary>
public record ConfirmEmailCommand(Guid UserId, string Token) : ICommand<ConfirmEmailResult>;

public record ConfirmEmailResult(string Message);
