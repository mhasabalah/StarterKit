using System.Linq.Expressions;
using Mapster;
using MapsterMapper;
using Shared.Contracts.Enums;
using Shared.Contracts.Pagination;

namespace Shared.Core.Extensions;

public static class QueryExtensions
{
    public static IQueryable<TDto> MapProjectTo<T, TDto>(
        this IQueryable<T> query,
        TypeAdapterConfig? config = null)
    {
        var mappedQuery = query.ProjectToType<TDto>(config);

        return mappedQuery;
    }

    public static Task<PaginatedResult<T>> PagedResult<T>(
        this IQueryable<T> query,
        int? pageIndex,
        int? pageSize,
        CancellationToken cancellationToken)
        => query.PagedResult(pageIndex ?? 1, pageSize ?? 10, cancellationToken);

    public static async Task<PaginatedResult<T>> PagedResult<T>(
        this IQueryable<T> query,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var skip = (pageIndex - 1) * pageSize;

        var pageInfoResult = new PageInfoResult
        {
            TotalItems = await query.CountAsync(cancellationToken),

            PageSize = pageSize,

            CurrentPage = pageIndex
        };

        var pagedQuery = pageSize != -1
            ? query
                .Skip(skip)
                .Take(pageSize)
            : query;

        var paginatedList = new PaginatedResult<T>
        {
            PageInfoResult = pageInfoResult,

            Result = await pagedQuery.ToListAsync(cancellationToken)
        };

        return paginatedList;
    }

    public static async Task<PaginatedResult<T>> PagedView<T>(
        this IQueryable<T> query,
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var skip = (pageIndex - 1) * pageSize;

        var pageInfoResult = new PageInfoResult
        {
            TotalItems = query.Count(),

            PageSize = pageSize,

            CurrentPage = pageIndex
        };

        var pagedQuery = pageSize != -1
            ? query
                .Skip(skip)
                .Take(pageSize)
            : query;

        var paginatedList = new PaginatedResult<T>
        {
            PageInfoResult = pageInfoResult,

            Result = pagedQuery.ToList()
        };

        return paginatedList;
    }

    public static IQueryable<T> OrderByDynamic<T>(this IQueryable<T> query, string? orderByProperty,
        SortBy? sort)
    {
        if (string.IsNullOrWhiteSpace(orderByProperty))
            return query;

        sort ??= SortBy.Descending;

        var entityType = typeof(T);
        var property = entityType.GetProperties()
            .FirstOrDefault(p =>
                string.Equals(p.Name, orderByProperty, StringComparison.OrdinalIgnoreCase));

        if (property == null)
            return query;

        var parameter = Expression.Parameter(entityType, "p");
        var propertyAccess = Expression.MakeMemberAccess(parameter, property);
        var orderByExpression = Expression.Lambda(propertyAccess, parameter);

        var methodName = sort == SortBy.Ascending ? "OrderBy" : "OrderByDescending";

        var resultExpression = Expression.Call(typeof(Queryable), methodName,
            new Type[] { entityType, property.PropertyType },
            query.Expression, Expression.Quote(orderByExpression));

        return query.Provider.CreateQuery<T>(resultExpression);
    }
}
