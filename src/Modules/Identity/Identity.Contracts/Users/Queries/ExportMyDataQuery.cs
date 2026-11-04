using Identity.Contracts.Users.Dtos;

namespace Identity.Contracts.Users.Queries;

/// <summary>The caller's personal-data export (<see cref="MyDataExportDto"/>).</summary>
public sealed record ExportMyDataQuery(Guid UserId) : IQuery<MyDataExportDto>;
