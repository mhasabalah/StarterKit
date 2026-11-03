using Microsoft.Extensions.Configuration;

namespace Shared.Core.Pagination;

/// <summary>
/// Global paging limits, bound from the <c>Paging</c> configuration section at startup
/// (<see cref="PagingOptionsExtensions.AddPaging"/>). Shared static state because the paging helpers are
/// extension methods on <see cref="IQueryable{T}"/> with no DI access.
/// </summary>
public static class PagingOptions
{
    public const string SectionName = "Paging";

    public const int DefaultPageSizeValue = 10;
    public const int DefaultMaxPageSize = 100;
    public const int DefaultAnonymousMaxPageSize = 20;

    /// <summary>Upper bound for any page size (config <c>Paging:MaxPageSize</c>, default 100).</summary>
    public static int MaxPageSize { get; private set; } = DefaultMaxPageSize;

    /// <summary>Page size used when the client sends none or a non-positive value (config <c>Paging:DefaultPageSize</c>).</summary>
    public static int DefaultPageSize { get; private set; } = DefaultPageSizeValue;

    /// <summary>Upper bound for anonymous callers on PageGate endpoints (config <c>Paging:AnonymousMaxPageSize</c>, default 20).</summary>
    public static int AnonymousMaxPageSize { get; private set; } = DefaultAnonymousMaxPageSize;

    public static void Configure(int maxPageSize, int defaultPageSize, int anonymousMaxPageSize)
    {
        if (maxPageSize < 1)
            throw new InvalidOperationException($"{SectionName}:MaxPageSize must be >= 1.");
        if (defaultPageSize < 1 || defaultPageSize > maxPageSize)
            throw new InvalidOperationException($"{SectionName}:DefaultPageSize must be between 1 and MaxPageSize.");
        if (anonymousMaxPageSize < 1 || anonymousMaxPageSize > maxPageSize)
            throw new InvalidOperationException($"{SectionName}:AnonymousMaxPageSize must be between 1 and MaxPageSize.");

        MaxPageSize = maxPageSize;
        DefaultPageSize = defaultPageSize;
        AnonymousMaxPageSize = anonymousMaxPageSize;
    }

    /// <summary>
    /// Clamps client input: page &lt; 1 (or null) → 1; size null/&lt;1 → <see cref="DefaultPageSize"/>;
    /// size &gt; <see cref="MaxPageSize"/> → <see cref="MaxPageSize"/>. There is no "all rows" value.
    /// </summary>
    public static (int PageIndex, int PageSize) Normalize(int? pageIndex, int? pageSize)
    {
        var page = pageIndex is null or < 1 ? 1 : pageIndex.Value;
        var size = pageSize is null or < 1 ? DefaultPageSize : Math.Min(pageSize.Value, MaxPageSize);
        return (page, size);
    }

    /// <summary>Rows to skip for a normalized page; saturates at <see cref="int.MaxValue"/> instead of overflowing.</summary>
    public static int Skip(int pageIndex, int pageSize)
    {
        var skip = ((long)pageIndex - 1) * pageSize;
        return skip > int.MaxValue ? int.MaxValue : (int)skip;
    }
}

public static class PagingOptionsExtensions
{
    public static IServiceCollection AddPaging(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PagingOptions.SectionName);
        PagingOptions.Configure(
            section.GetValue("MaxPageSize", PagingOptions.DefaultMaxPageSize),
            section.GetValue("DefaultPageSize", PagingOptions.DefaultPageSizeValue),
            section.GetValue("AnonymousMaxPageSize", PagingOptions.DefaultAnonymousMaxPageSize));
        return services;
    }
}
