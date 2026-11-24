using Hangfire;
using Microsoft.Extensions.Options;
using Search.Domains.Indexes.Features.SearchIndexes;
using Search.Domains.Indexes.Jobs;

namespace Search.Domains.Platform.Features.SearchPlatform;

/// <summary>The configured engine and the registered definitions. Reads no tenant data (host scope).</summary>
public sealed class GetSearchEngineHandler(SearchIndexRegistry registry, IOptions<SearchOptions> options)
    : IQueryHandler<GetSearchEngineQuery, SearchEngineDto>
{
    public Task<SearchEngineDto> Handle(GetSearchEngineQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new SearchEngineDto(
            options.Value.Engine.ToString(),
            registry.Indexes
                .OrderBy(index => index.Key, StringComparer.Ordinal)
                .Select(index => new SearchIndexDefinitionDto(
                    index.Key,
                    index.Definition.Name,
                    index.Fingerprint,
                    SearchIndexMapping.TextFields(index),
                    SearchIndexMapping.FilterFields(index)))
                .ToList()));
}

/// <summary>
/// Queues <see cref="ReconcileSearchIndexesJob.RebuildAllTenantsAsync"/>, which fans out one forced rebuild per tenant.
/// Enqueued from the host scope, so the fan-out job runs without a tenant and visits every one.
/// </summary>
public sealed class RebuildSearchIndexInAllTenantsHandler(
    SearchIndexRegistry registry, IBackgroundJobClient jobs, IStringLocalizer<SearchResource> localizer)
    : ICommandHandler<RebuildSearchIndexInAllTenantsCommand, RebuildSearchIndexInAllTenantsResult>
{
    public Task<RebuildSearchIndexInAllTenantsResult> Handle(
        RebuildSearchIndexInAllTenantsCommand command, CancellationToken cancellationToken)
    {
        var key = SearchIndexMapping.FindOrThrow(registry, command.IndexKey, localizer).Key;
        jobs.Enqueue<ReconcileSearchIndexesJob>(job => job.RebuildAllTenantsAsync(key, JobCancellationToken.Null));
        return Task.FromResult(new RebuildSearchIndexInAllTenantsResult(key));
    }
}
