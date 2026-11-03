namespace Shared.Core.Extensions;

/// <summary>
/// Normalizes client-supplied filter bounds for timestamptz columns. Npgsql only accepts UTC <see cref="DateTime"/>
/// (Kind=Utc) and <see cref="DateTimeOffset"/> with offset 0 as timestamptz parameters; anything else throws at query
/// time (a 500 instead of results). A query-string date such as <c>?from=2026-11-01</c> binds as
/// <see cref="DateTimeKind.Unspecified"/>, which is taken to mean UTC.
/// </summary>
public static class UtcDateTimeExtensions
{
    public static DateTime AsUtc(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static DateTime? AsUtc(this DateTime? value) => value?.AsUtc();

    public static DateTimeOffset? AsUtc(this DateTimeOffset? value) => value?.ToUniversalTime();
}
