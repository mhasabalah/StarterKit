namespace Identity.Contracts.Users.Dtos;

public record MyProfileDto
{
    public Guid Id { get; init; }
    public string Email { get; init; } = default!;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string FullName { get; init; } = default!;
    public string? PhoneNumber { get; init; }
    public string? ProfilePictureUrl { get; init; }
    public bool IsProfileComplete { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
    public DateTime CreatedAt { get; init; }
}
