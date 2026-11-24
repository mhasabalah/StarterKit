using Search.Domains.Analytics.Models;

namespace Search.Data.Configurations;

public sealed class SearchDailyStatsConfiguration : IEntityTypeConfiguration<SearchDailyStats>
{
    public void Configure(EntityTypeBuilder<SearchDailyStats> builder)
    {
        builder.ToTable("search_daily_stats");
        builder.HasKey(s => new { s.TenantId, s.IndexKey, s.Day });
        builder.Property(s => s.IndexKey).HasMaxLength(SearchIndexDefinition.KeyMaxLength);

        // The analytics queries and the retention purge read by day across indexes.
        builder.HasIndex(s => new { s.TenantId, s.Day });
    }
}

public sealed class SearchZeroResultTermConfiguration : IEntityTypeConfiguration<SearchZeroResultTerm>
{
    public void Configure(EntityTypeBuilder<SearchZeroResultTerm> builder)
    {
        builder.ToTable("search_zero_result_terms");
        builder.HasKey(t => new { t.TenantId, t.IndexKey, t.Day, t.Term });
        builder.Property(t => t.IndexKey).HasMaxLength(SearchIndexDefinition.KeyMaxLength);
        builder.Property(t => t.Term).HasMaxLength(SearchZeroResultTerm.TermMaxLength);

        builder.HasIndex(t => new { t.TenantId, t.Day });
    }
}
