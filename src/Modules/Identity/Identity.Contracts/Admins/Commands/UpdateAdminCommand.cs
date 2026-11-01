using Identity.Contracts.Admins.Dtos;

namespace Identity.Contracts.Admins.Commands;

public record UpdateAdminCommand : ICommand<AdminDto>
{
    public Guid AdminId { get; init; }
    public string Email { get; init; } = default!;
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
}
