using System.ComponentModel;

namespace Shared.Contracts.CQRS;

public record BaseQuery : BaseSortQuery
{
    [DefaultValue(1)]
    public int? PageNumber { get; set; } = 1;

    [DefaultValue(10)]
    public int? PageSize { get; set; } = 10;
}
