using Identity.Contracts.Admins.Dtos;

namespace Identity.Contracts.Admins.Queries;

public record GetAdminsQuery : BaseQuery, IQuery<PaginatedResult<AdminListDto>>
{
    public string? SearchTerm { get; init; }
    public bool? IsActive { get; init; }
    public bool? IsLocked { get; init; }
    public bool? IsSuperAdmin { get; init; }
}
