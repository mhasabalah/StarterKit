using Identity.Contracts.Users.Dtos;

namespace Identity.Contracts.Users.Queries;

public record GetUsersQuery : BaseQuery, IQuery<PaginatedResult<UserListDto>>
{
    public string? SearchTerm { get; init; }
    public bool? IsActive { get; init; }
    public bool? IsLocked { get; init; }
    public bool? EmailConfirmed { get; init; }
    public string? Role { get; init; }
    public string? OAuthProvider { get; init; }
    public DateTime? CreatedFrom { get; init; }
    public DateTime? CreatedTo { get; init; }
    public DateTime? LastLoginFrom { get; init; }
    public DateTime? LastLoginTo { get; init; }
}
