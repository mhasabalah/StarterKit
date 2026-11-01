using System.Globalization;
using Shared.Contracts.Enums;

namespace Shared.Contracts.Pagination;

public class PaginatedResult<T>
{
    public PageInfoResult PageInfoResult { get; set; }
    public string? OrderBy { get; set; }
    public bool OrderByDesc { get; set; }
    public List<T>? Result { get; set; }
}

public class PageInfoResult
{
    public int CurrentPage { get; set; }

    public int PageSize { get; set; }

    public int TotalItems { get; set; }
}
