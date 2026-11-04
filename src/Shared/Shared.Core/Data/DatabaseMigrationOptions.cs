using Microsoft.Extensions.Configuration;

namespace Shared.Core.Data;

/// <summary>
/// Controls WHEN and HOW the schema is migrated and seeded (configuration section <c>Database</c>).
/// <list type="bullet">
/// <item><c>MigrateOnStartup</c> (default <c>true</c>: dev, Aspire, tests): every app instance migrates + seeds at boot.
/// Production sets it to <c>false</c> and runs the one-shot migrator (<c>MigrateOnly</c>) at deploy time instead.</item>
/// <item><c>MigrateOnly</c> (CLI: <c>dotnet Api.dll --migrate-only</c>): migrate + seed (shared and dedicated tenant
/// databases), then exit; implies migrating regardless of <c>MigrateOnStartup</c>.</item>
/// <item><c>MigrationConnectionString</c>: optional, more privileged connection string used ONLY to apply migrations of
/// the shared database, so the runtime role can be DDL-free. Falls back to the normal connection string.</item>
/// <item><c>LockTimeoutSeconds</c>: how long to wait for the per-database advisory lock held by another migrator.</item>
/// </list>
/// </summary>
public sealed class DatabaseMigrationOptions
{
    public const string SectionName = "Database";
    public const string MigrateOnlyArgument = "--migrate-only";

    public bool MigrateOnStartup { get; set; } = true;
    public bool MigrateOnly { get; set; }
    public string? MigrationConnectionString { get; set; }
    public int LockTimeoutSeconds { get; set; } = 300;

    /// <summary>True when this process must migrate + seed (boot-time migration or the migrate-only run).</summary>
    public bool ShouldMigrate => MigrateOnStartup || MigrateOnly;

    public static DatabaseMigrationOptions From(IConfiguration configuration) =>
        configuration.GetSection(SectionName).Get<DatabaseMigrationOptions>() ?? new DatabaseMigrationOptions();
}
