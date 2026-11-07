using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Shared.Core.Data;

/// <summary>
/// The storage precision of every money column: <c>numeric(19,4)</c>. Four decimals hold every ISO 4217 currency
/// (0 for JPY, 2 for USD, 3 for KWD, 4 for CLF) without rounding on the way into Postgres; the per-currency rounding
/// itself happens in the domain (Subscriptions <c>Currency</c>), never in the column. Percentages and tax rates are NOT
/// money and keep their own precision.
/// </summary>
public static class MoneyPrecision
{
    public const int Precision = 19;
    public const int Scale = 4;

    /// <summary>Maps a money property to <c>numeric(19,4)</c>. Use this on every amount / price column.</summary>
    public static PropertyBuilder<TProperty> HasMoneyPrecision<TProperty>(this PropertyBuilder<TProperty> builder) =>
        builder.HasPrecision(Precision, Scale);
}
