namespace Identity.Contracts.Auth.Commands;

public record ExternalLoginCommand(
    string Provider,
    string ReturnUrl
) : ICommand<ExternalLoginResult>;

public record ExternalLoginResult(
    string RedirectUrl
);

public record ExternalLoginCallbackCommand(
    string? RemoteError
) : ICommand<ExternalLoginCallbackResult>;

public record ExternalLoginCallbackResult(
    Guid UserId,
    bool IsNewUser,
    bool RequiresPhoneVerification,
    string? AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt,
    string Message
);
