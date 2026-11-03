namespace Catalog.Common;

internal static class PaginatedResultExtensions
{
    /// <summary>Maps a page of rows (projected in SQL) to DTOs in memory, keeping the paging metadata.</summary>
    public static PaginatedResult<TDest> Map<TSource, TDest>(
        this PaginatedResult<TSource> source, Func<TSource, TDest> map) => new()
    {
        PageInfoResult = source.PageInfoResult,
        OrderBy = source.OrderBy,
        OrderByDesc = source.OrderByDesc,
        Result = (source.Result ?? []).Select(map).ToList()
    };

    /// <summary>
    /// Applies the client sort (<see cref="BaseSortQuery.SortColumn"/>, matched case-insensitively against
    /// the row's properties) or <paramref name="defaultOrder"/> when none/unknown, then a stable
    /// tie-breaker so pages never overlap.
    /// </summary>
    public static IQueryable<T> ApplySort<T, TKey>(
        this IQueryable<T> query,
        BaseSortQuery sort,
        Func<IQueryable<T>, IOrderedQueryable<T>> defaultOrder,
        System.Linq.Expressions.Expression<Func<T, TKey>> tieBreaker)
    {
        // The module default order goes first. Shared OrderByDynamic then either replaces it with a valid client
        // column (+ Id tie-breaker), or keeps it for a missing/unknown column and appends Id. Either way the order
        // is deterministic and the module default wins when the client asks for nothing.
        var withDefault = defaultOrder(query).ThenBy(tieBreaker);
        return string.IsNullOrWhiteSpace(sort.SortColumn)
            ? withDefault
            : withDefault.OrderByDynamic(sort.SortColumn, sort.SortBy);
    }
}
