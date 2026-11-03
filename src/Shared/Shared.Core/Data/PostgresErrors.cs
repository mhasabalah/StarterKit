using Npgsql;

namespace Shared.Core.Data;

/// <summary>
/// Handlers pre-check uniqueness for a friendly error, but two concurrent requests can both pass the
/// check. The unique index is the real guard: translate its violation (SQLSTATE 23505) into HTTP 409.
/// Shared by every module (Catalog, Tenancy, ...).
/// </summary>
public static class PostgresErrors
{
    public const string UniqueViolation = "23505";

    /// <param name="constraintName">When given, only a violation of this index/constraint matches.</param>
    public static bool IsUniqueViolation(this DbUpdateException exception, string? constraintName = null) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolation } pg &&
        (constraintName is null || string.Equals(pg.ConstraintName, constraintName, StringComparison.Ordinal));
}
