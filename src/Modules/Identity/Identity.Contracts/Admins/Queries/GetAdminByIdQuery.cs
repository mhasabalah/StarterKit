using Identity.Contracts.Admins.Dtos;

namespace Identity.Contracts.Admins.Queries;

/// <summary>
/// Query to get admin details by ID
/// </summary>
public record GetAdminByIdQuery(Guid AdminId) : IQuery<AdminDto>;
