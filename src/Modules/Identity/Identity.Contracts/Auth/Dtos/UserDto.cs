namespace Identity.Contracts.Auth.Dtos;

public record UserDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = default!;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
    public bool IsEmailVerified { get; init; }
    public bool IsPhoneVerified { get; init; }
    public bool IsProfileComplete { get; init; }
    public List<string> Roles { get; init; } = new();
}
