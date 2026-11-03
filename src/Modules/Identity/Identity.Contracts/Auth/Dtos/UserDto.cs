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

    /// <summary>
    /// Platform permissions derived from <see cref="Roles"/> (the access token carries roles only). Use these to
    /// show/hide UI; the server re-checks every request. Tenant permissions come from the Tenancy <c>GetMyMembership</c> endpoint.
    /// </summary>
    public List<string> Permissions { get; init; } = new();
}
