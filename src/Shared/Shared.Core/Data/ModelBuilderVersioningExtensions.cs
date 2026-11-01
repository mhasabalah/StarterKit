namespace Shared.Core.Data;

public static class ModelBuilderVersioningExtensions
{
    /// <summary>
    /// Marks the Version property on every Entity-derived type as an EF concurrency token.
    /// Call this at the end of OnModelCreating in every module DbContext.
    /// </summary>
    public static void ConfigureVersionConcurrencyTokens(this ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            var versionProp = entityType.FindProperty("Version");
            if (versionProp is not null && versionProp.ClrType == typeof(long))
            {
                versionProp.IsConcurrencyToken = true;
            }
        }
    }
}
