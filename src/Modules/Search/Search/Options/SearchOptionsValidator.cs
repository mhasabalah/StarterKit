using Microsoft.Extensions.Options;

namespace Search.Options;

/// <summary>Validates <see cref="SearchOptions"/> at startup (<c>ValidateOnStart</c>): a bad value stops the host.</summary>
public sealed class SearchOptionsValidator : IValidateOptions<SearchOptions>
{
    public ValidateOptionsResult Validate(string? name, SearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        void Range(int value, int min, int max, string key)
        {
            if (value < min || value > max)
                errors.Add($"{SearchOptions.SectionName}:{key} must be between {min} and {max} (was {value}).");
        }

        void Cron(string? value, string key)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is not (5 or 6))
                errors.Add($"{SearchOptions.SectionName}:{key} must be a cron expression (was '{value}').");
        }

        if (!Enum.IsDefined(options.Engine))
            errors.Add($"{SearchOptions.SectionName}:Engine '{options.Engine}' is not an engine (Postgres, Meilisearch).");

        Range(options.Indexing.BatchSize, 1, 5000, "Indexing:BatchSize");
        Range(options.Indexing.LeaseMinutes, 1, 24 * 60, "Indexing:LeaseMinutes");

        Range(options.Query.MaxPageSize, 1, 1000, "Query:MaxPageSize");
        Range(options.Query.MaxWindow, options.Query.MaxPageSize, 100_000, "Query:MaxWindow");
        Range(options.Query.MaxTextLength, 1, 1000, "Query:MaxTextLength");
        Range(options.Query.MaxTerms, 1, 32, "Query:MaxTerms");
        Range(options.Query.Fuzzy.MinTermLength, 1, 20, "Query:Fuzzy:MinTermLength");
        if (options.Query.Fuzzy.Threshold is <= 0 or > 1)
            errors.Add($"{SearchOptions.SectionName}:Query:Fuzzy:Threshold must be greater than 0 and at most 1 (was {options.Query.Fuzzy.Threshold}).");

        Range(options.Facets.MaxValues, 1, 200, "Facets:MaxValues");
        Range(options.Facets.ScanLimit, 1, 1_000_000, "Facets:ScanLimit");

        Range(options.Postgres.StatementTimeoutMs, 100, 60_000, "Postgres:StatementTimeoutMs");

        Cron(options.Reconcile.Cron, "Reconcile:Cron");
        Cron(options.Drift.Cron, "Drift:Cron");
        Range(options.Drift.MaxDocuments, 1, 100_000_000, "Drift:MaxDocuments");

        Range(options.Analytics.RetentionDays, 1, 3650, "Analytics:RetentionDays");
        Range(options.Analytics.MaxQueryLength, 2, 200, "Analytics:MaxQueryLength");
        Range(options.Analytics.FlushSeconds, 1, 3600, "Analytics:FlushSeconds");
        Range(options.Analytics.MaxPendingTerms, 100, 1_000_000, "Analytics:MaxPendingTerms");
        Cron(options.Analytics.PurgeCron, "Analytics:PurgeCron");

        var meili = options.Meilisearch;
        Range(meili.TimeoutSeconds, 1, 300, "Meilisearch:TimeoutSeconds");
        Range(meili.TaskTimeoutSeconds, 1, 3600, "Meilisearch:TaskTimeoutSeconds");
        if (meili.IndexPrefix.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            errors.Add($"{SearchOptions.SectionName}:Meilisearch:IndexPrefix may contain only letters, digits, '_' and '-'.");

        if (options.Engine == SearchEngineKind.Meilisearch)
        {
            if (!Uri.TryCreate(meili.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
                errors.Add($"{SearchOptions.SectionName}:Meilisearch:Url must be an absolute http(s) URL with Engine=Meilisearch.");
            if (string.IsNullOrWhiteSpace(meili.ApiKey))
                errors.Add($"{SearchOptions.SectionName}:Meilisearch:ApiKey is required with Engine=Meilisearch.");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
