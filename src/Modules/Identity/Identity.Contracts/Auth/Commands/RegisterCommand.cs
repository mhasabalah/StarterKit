using Identity.Contracts.Auth.Dtos;

namespace Identity.Contracts.Auth.Commands;

public record RegisterCommand(
    string Identifier,          // Email OR Phone
    string Password,
    string ConfirmPassword,
    string? FirstName,
    string? LastName
) : ICommand<RegisterResult>;

public record RegisterResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User,
    string Message
);

public enum IdentifierType
{
    Email,
    Phone
}
