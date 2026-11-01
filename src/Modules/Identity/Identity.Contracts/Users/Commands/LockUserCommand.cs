namespace Identity.Contracts.Users.Commands;

/// <summary>
/// Command to lock/suspend a user account
/// </summary>
public record LockUserCommand : ICommand<LockUserResult>
{
    public Guid UserId { get; init; }
    public string Reason { get; init; } = default!;
    public LockDuration Duration { get; init; } = LockDuration.Permanent;
    public int? CustomDurationHours { get; init; }
    public Guid LockedByAdminId { get; init; }
}

public record LockUserResult(
    bool Success,
    string Message,
    DateTimeOffset? LockoutEnd
);

public enum LockDuration
{
    OneHour,
    OneDay,
    OneWeek,
    OneMonth,
    Permanent,
    Custom
}
