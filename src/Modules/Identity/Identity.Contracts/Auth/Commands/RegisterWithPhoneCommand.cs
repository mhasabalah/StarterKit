namespace Identity.Contracts.Auth.Commands;

public record RegisterWithPhoneCommand(
    string PhoneNumber,
    string Password,
    string ConfirmPassword,
    string? FirstName,
    string? LastName
) : ICommand<RegisterResult>;
