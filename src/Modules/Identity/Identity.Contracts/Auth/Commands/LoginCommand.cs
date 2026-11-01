using Identity.Contracts.Auth.Dtos;

namespace Identity.Contracts.Auth.Commands;

public record LoginCommand(
    string Identifier,  // Email OR Phone
    string Password
) : ICommand<LoginResult>;

public record LoginResult(
    string? AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt,
    UserDto User,
    string Message
);
