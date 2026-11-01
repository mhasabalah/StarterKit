namespace Identity.Contracts.Admins.Commands;

public record AdminChangePasswordCommand : ICommand
{
    public Guid AdminId { get; init; }
    public string NewPassword { get; init; } = default!;
    public Guid ChangedByAdminId { get; init; }
}
