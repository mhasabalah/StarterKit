using Identity.Contracts.Audit;
using Identity.Contracts.Users.Dtos;

namespace Identity.Contracts.Users.Commands;

/// <summary>
/// Command for an admin to edit a user's core profile fields.
/// </summary>
public record EditUserCommand : ICommand<UserDto>, IAuditedCommand
{
    public Guid UserId { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string Email { get; init; } = default!;
    public string? PhoneNumber { get; init; }
}
