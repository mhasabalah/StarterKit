namespace Identity.Contracts.Auth.Commands;

public record CompleteProfileCommand(
    string? FirstName,
    string? LastName,
    string? Email,              // Optional: Add email if registered with phone
    string? PhoneNumber,        // Optional: Add phone if registered with email
    string? ProfilePictureUrl
) : ICommand<CompleteProfileResult>;

public record CompleteProfileResult(
    bool IsComplete,
    bool RequiresPhoneVerification,
    bool RequiresEmailVerification,
    string Message
);
