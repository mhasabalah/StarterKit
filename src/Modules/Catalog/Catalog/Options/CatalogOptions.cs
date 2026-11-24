namespace Catalog.Options;

/// <summary>Bound from the "Catalog" section of appsettings.json.</summary>
public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    /// <summary>Soft-deleted products older than this are hard-deleted by PurgeSoftDeletedProductsJob.</summary>
    public int SoftDeleteRetentionDays { get; set; } = 30;

    /// <summary>Max products purged per job run (keeps each run short).</summary>
    public int PurgeBatchSize { get; set; } = 100;

    /// <summary>Hangfire cron for the purge job (UTC).</summary>
    public string PurgeCron { get; set; } = "0 3 * * *";
#if (SEARCH)

    public CatalogSearchOptions Search { get; set; } = new();
#endif
}
