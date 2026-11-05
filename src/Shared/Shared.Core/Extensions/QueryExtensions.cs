using System.Linq.Expressions;
using Mapster;
using Microsoft.EntityFrameworkCore.Query;
using Shared.Contracts.Enums;
using Shared.Contracts.Pagination;
using Shared.Core.Pagination;

namespace Shared.Core.Extensions;

public static class QueryExtensions
{
    private const string IdProperty = "Id";
    private const string CreatedAtProperty = "CreatedAt";

    private static readonly HashSet<string> OrderingMethods =
        ["OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending"];

    public static IQueryable<TDto> MapProjectTo<T, TDto>(
        this IQueryable<T> query,
        TypeAdapterConfig? config = null)
    {
        var mappedQuery = query.ProjectToType<TDto>(config);

        return mappedQuery;
    }

    /// <summary>
    /// Pages a query. Input is clamped by <see cref="PagingOptions.Normalize"/>:
    /// page &lt; 1 → 1, size &lt; 1 → default, size &gt; <c>Paging:MaxPageSize</c> → max. There is no "return all" value
    /// (the old <c>pageSize = -1</c> bypass is gone); internal callers that need every row must iterate pages or
    /// query without paging explicitly.
    /// </summary>
    public static Task<PaginatedResult<T>> PagedResult<T>(
        this IQueryable<T> query,
        int? pageIndex,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var (page, size) = PagingOptions.Normalize(pageIndex, pageSize);
        return query.PagedResultCore(page, size, cancellationToken);
    }

    /// <inheritdoc cref="PagedResult{T}(IQueryable{T},int?,int?,CancellationToken)"/>
    public static Task<PaginatedResult<T>> PagedResult<T>(
        this IQueryable<T> query,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
        => query.PagedResult((int?)pageIndex, (int?)pageSize, cancellationToken);

    /// <summary>Synchronous variant of <see cref="PagedResult{T}(IQueryable{T},int?,int?,CancellationToken)"/> (same clamping).</summary>
    public static Task<PaginatedResult<T>> PagedView<T>(
        this IQueryable<T> query,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var (page, size) = PagingOptions.Normalize(pageIndex, pageSize);
        var skip = PagingOptions.Skip(page, size);

        return Task.FromResult(new PaginatedResult<T>
        {
            PageInfoResult = new PageInfoResult { TotalItems = query.Count(), PageSize = size, CurrentPage = page },
            Result = query.Skip(skip).Take(size).ToList()
        });
    }

    private static async Task<PaginatedResult<T>> PagedResultCore<T>(
        this IQueryable<T> query,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        var skip = PagingOptions.Skip(page, size);
        var pagedQuery = query.Skip(skip).Take(size);

        // EF queries run async; LINQ-to-objects (tests, in-memory projections) falls back to sync.
        var isAsync = query.Provider is IAsyncQueryProvider;

        var total = isAsync ? await query.CountAsync(cancellationToken) : query.Count();
        var rows = isAsync ? await pagedQuery.ToListAsync(cancellationToken) : pagedQuery.ToList();

        return new PaginatedResult<T>
        {
            PageInfoResult = new PageInfoResult { TotalItems = total, PageSize = size, CurrentPage = page },
            Result = rows
        };
    }

    /// <summary>
    /// Orders by a client-supplied column name (case-insensitive) and ALWAYS yields a deterministic order:
    /// <list type="number">
    /// <item>Known, sortable column → order by it, then by <c>Id</c> (same direction) as tie-breaker.</item>
    /// <item>Missing / unknown / non-sortable column → if the query is already ordered, that order is kept and
    /// <c>Id</c> is appended as tie-breaker; otherwise order by <paramref name="defaultProperty"/>, else
    /// <c>CreatedAt</c>, else <c>Id</c>.</item>
    /// </list>
    /// Only scalar properties (numbers, strings, dates, Guid, enums, bool and their nullable forms) are sortable,
    /// so a navigation/collection name can't produce an untranslatable query (500).
    /// </summary>
    public static IQueryable<T> OrderByDynamic<T>(
        this IQueryable<T> query,
        string? orderByProperty,
        SortBy? sort,
        string? defaultProperty = null)
    {
        var descending = (sort ?? SortBy.Descending) == SortBy.Descending;
        var idProperty = FindSortableProperty<T>(IdProperty);

        var requested = FindSortableProperty<T>(orderByProperty);
        if (requested is null && IsOrdered(query))
        {
            return idProperty is null ? query : ApplyOrder(query, idProperty, descending, thenBy: true);
        }

        var primary = requested
                      ?? FindSortableProperty<T>(defaultProperty)
                      ?? FindSortableProperty<T>(CreatedAtProperty)
                      ?? idProperty;

        if (primary is null)
            return query;

        var ordered = ApplyOrder(query, primary, descending, thenBy: false);

        if (idProperty is not null && idProperty != primary)
            ordered = ApplyOrder(ordered, idProperty, descending, thenBy: true);

        return ordered;
    }

    private static bool IsOrdered<T>(IQueryable<T> query) =>
        query.Expression is MethodCallExpression call &&
        call.Method.DeclaringType == typeof(Queryable) &&
        OrderingMethods.Contains(call.Method.Name);

    private static PropertyInfo? FindSortableProperty<T>(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var property = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        return property is not null && property.CanRead && IsSortableType(property.PropertyType) ? property : null;
    }

    private static bool IsSortableType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive || type.IsEnum ||
               type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
               type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
               type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan);
    }

    private static IQueryable<T> ApplyOrder<T>(IQueryable<T> query, PropertyInfo property, bool descending, bool thenBy)
    {
        var parameter = Expression.Parameter(typeof(T), "p");
        var lambda = Expression.Lambda(Expression.MakeMemberAccess(parameter, property), parameter);

        var methodName = (thenBy, descending) switch
        {
            (false, false) => nameof(Queryable.OrderBy),
            (false, true) => nameof(Queryable.OrderByDescending),
            (true, false) => nameof(Queryable.ThenBy),
            (true, true) => nameof(Queryable.ThenByDescending)
        };

        var call = Expression.Call(typeof(Queryable), methodName,
            [typeof(T), property.PropertyType],
            query.Expression, Expression.Quote(lambda));

        return query.Provider.CreateQuery<T>(call);
    }
}
