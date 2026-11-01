using System.ComponentModel;
using Shared.Contracts.Enums;

namespace Shared.Contracts.CQRS;

public record BaseSortQuery
{
    public SortBy? SortBy { get; set; } = Enums.SortBy.Descending;

    [DefaultValue(null)]
    public string? SortColumn { get; set; }
}
