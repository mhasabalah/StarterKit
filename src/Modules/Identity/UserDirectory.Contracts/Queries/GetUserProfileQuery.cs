namespace UserDirectory.Contracts.Queries;

/// <summary>
/// Display + contact data of ONE user (e.g. to address a billing email). Returns <c>null</c> when the user does not
/// exist - callers degrade gracefully instead of catching "not found" exceptions.
/// </summary>
public sealed record GetUserProfileQuery(Guid UserId) : IQuery<UserProfileDto?>;

/// <param name="DisplayName">Same rule as <see cref="UserSummaryDto.DisplayName"/> - never empty.</param>
public sealed record UserProfileDto(Guid UserId, string DisplayName, string? Email, string? Phone, bool IsActive);
