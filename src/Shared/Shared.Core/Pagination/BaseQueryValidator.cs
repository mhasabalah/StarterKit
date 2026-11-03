using Shared.Contracts.Enums;

namespace Shared.Core.Pagination;

/// <summary>
/// Reusable paging/sorting rules for any <see cref="BaseQuery"/>. Rejects bad input with 400 at the boundary
/// (the paging helpers additionally clamp, as defence in depth).
/// <para>Usage in a module: <c>public sealed class GetXsQueryValidator : BaseQueryValidator&lt;GetXsQuery&gt; { ... }</c>
/// or, inside an existing validator, <c>Include(new BaseQueryValidator&lt;GetXsQuery&gt;());</c>.</para>
/// </summary>
public class BaseQueryValidator<TQuery> : AbstractValidator<TQuery> where TQuery : BaseQuery
{
    public BaseQueryValidator()
    {
        RuleFor(q => q.PageNumber).ValidPageNumber();
        RuleFor(q => q.PageSize).ValidPageSize();
        RuleFor(q => q.SortColumn).ValidSortColumn();
        RuleFor(q => q.SortBy).IsInEnum().When(q => q.SortBy.HasValue);
    }
}

public static class PagingValidationExtensions
{
    public const int MaxSortColumnLength = 64;

    /// <summary>Null (= default) or &gt;= 1.</summary>
    public static IRuleBuilderOptions<T, int?> ValidPageNumber<T>(this IRuleBuilder<T, int?> rule) =>
        rule.Must(v => v is null or >= 1)
            .WithMessage("PageNumber must be greater than or equal to 1.");

    /// <summary>Null (= default) or between 1 and <see cref="PagingOptions.MaxPageSize"/> (evaluated at validation time).</summary>
    public static IRuleBuilderOptions<T, int?> ValidPageSize<T>(this IRuleBuilder<T, int?> rule) =>
        rule.Must(v => v is null || (v >= 1 && v <= PagingOptions.MaxPageSize))
            .WithMessage(_ => $"PageSize must be between 1 and {PagingOptions.MaxPageSize}.");

    /// <summary>Null/empty, or a plain identifier (letters, digits, underscore) of at most 64 chars.</summary>
    public static IRuleBuilderOptions<T, string?> ValidSortColumn<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => string.IsNullOrEmpty(v) ||
                       (v.Length <= MaxSortColumnLength && v.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')))
            .WithMessage($"SortColumn must be a property name (letters, digits, '_', max {MaxSortColumnLength} chars).");
}
