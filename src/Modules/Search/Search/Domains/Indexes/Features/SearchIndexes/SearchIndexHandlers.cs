using Hangfire;
using Search.Domains.Indexes.Jobs;
using Search.Domains.Indexes.Models;

namespace Search.Domains.Indexes.Features.SearchIndexes;

/// <summary>The registered indexes, each with its build state in the current tenant (not built when it has none).</summary>
public sealed class GetSearchIndexesHandler(SearchIndexRegistry registry, SearchDbContext db)
    : IQueryHandler<GetSearchIndexesQuery, List<SearchIndexDto>>
{
    public async Task<List<SearchIndexDto>> Handle(GetSearchIndexesQuery query, CancellationToken cancellationToken)
    {
        // Bounded: one row per registered index. States of definitions no module registers any more are not shown.
        var states = await db.IndexStates.AsNoTracking().ToDictionaryAsync(s => s.IndexKey, cancellationToken);
        return registry.Indexes
            .OrderBy(index => index.Key, StringComparer.Ordinal)
            .Select(index => SearchIndexMapping.ToDto(index, states.GetValueOrDefault(index.Key)))
            .ToList();
    }
}

public sealed class GetSearchIndexByKeyHandler(
    SearchIndexRegistry registry, SearchDbContext db, IStringLocalizer<SearchResource> localizer)
    : IQueryHandler<GetSearchIndexByKeyQuery, SearchIndexDto>
{
    public async Task<SearchIndexDto> Handle(GetSearchIndexByKeyQuery query, CancellationToken cancellationToken)
    {
        var index = SearchIndexMapping.FindOrThrow(registry, query.IndexKey, localizer);
        var state = await db.IndexStates.AsNoTracking().FirstOrDefaultAsync(s => s.IndexKey == index.Key, cancellationToken);
        return SearchIndexMapping.ToDto(index, state);
    }
}

/// <summary>
/// Queues <see cref="RebuildSearchIndexJob"/> (forced) for the current tenant. It reads the state only to refuse a
/// second rebuild while one holds the lease; the job owns every write (lease, stamps, sweep).
/// </summary>
public sealed class RebuildSearchIndexHandler(
    SearchIndexRegistry registry,
    SearchDbContext db,
    IBackgroundJobClient jobs,
    TimeProvider time,
    IStringLocalizer<SearchResource> localizer)
    : ICommandHandler<RebuildSearchIndexCommand, RebuildSearchIndexResult>
{
    public async Task<RebuildSearchIndexResult> Handle(RebuildSearchIndexCommand command, CancellationToken cancellationToken)
    {
        var index = SearchIndexMapping.FindOrThrow(registry, command.IndexKey, localizer);
        var state = await db.IndexStates.AsNoTracking().FirstOrDefaultAsync(s => s.IndexKey == index.Key, cancellationToken);
        if (state is not null && state.IsLeased(time.GetUtcNow().UtcDateTime))
        {
            throw new ConflictException(localizer["Index.RebuildRunning", index.Key])
            {
                ErrorCode = SearchIndexMapping.RebuildRunningErrorCode
            };
        }

        var key = index.Key;
        jobs.Enqueue<RebuildSearchIndexJob>(job => job.ExecuteAsync(key, true, JobCancellationToken.Null));
        return new RebuildSearchIndexResult(key);
    }
}

/// <summary>Queues <see cref="DetectSearchDriftJob.CheckIndexAsync"/> for the current tenant (a built index only).</summary>
public sealed class CheckSearchIndexDriftHandler(
    SearchIndexRegistry registry,
    SearchDbContext db,
    IBackgroundJobClient jobs,
    IStringLocalizer<SearchResource> localizer)
    : ICommandHandler<CheckSearchIndexDriftCommand, CheckSearchIndexDriftResult>
{
    public async Task<CheckSearchIndexDriftResult> Handle(CheckSearchIndexDriftCommand command, CancellationToken cancellationToken)
    {
        var index = SearchIndexMapping.FindOrThrow(registry, command.IndexKey, localizer);
        var built = await db.IndexStates.AsNoTracking()
            .AnyAsync(s => s.IndexKey == index.Key && s.LastBuiltAt != null, cancellationToken);
        if (!built)
        {
            throw new ConflictException(localizer["Index.NotBuilt", index.Key])
            {
                ErrorCode = SearchIndexMapping.NotBuiltErrorCode
            };
        }

        var key = index.Key;
        jobs.Enqueue<DetectSearchDriftJob>(job => job.CheckIndexAsync(key, JobCancellationToken.Null));
        return new CheckSearchIndexDriftResult(key);
    }
}

internal static class SearchIndexMapping
{
    public const string RebuildRunningErrorCode = "search_rebuild_running";
    public const string NotBuiltErrorCode = "search_index_not_built";

    public static RegisteredIndex FindOrThrow(
        SearchIndexRegistry registry, string? key, IStringLocalizer<SearchResource> localizer) =>
        (key is null ? null : registry.Find(key))
        ?? throw new NotFoundException(localizer["Index.NotFound", key ?? string.Empty]);

    public static SearchIndexDto ToDto(RegisteredIndex index, SearchIndexState? state) => new()
    {
        Key = index.Key,
        Name = index.Definition.Name,
        Status = state?.Status ?? SearchIndexStatus.NotBuilt,
        UpToDate = state is not null && !state.IsStale(index.Fingerprint),
        DocumentCount = state?.DocumentCount ?? 0,
        LastBuiltAt = state?.LastBuiltAt,
        BuildStartedAt = state?.BuildStartedAt,
        LastErrorCode = state?.LastErrorCode,
        Drift = state?.DriftCheckedAt is { } checkedAt
            ? new SearchIndexDriftDto(checkedAt, state.DriftMissing, state.DriftStale, state.DriftOrphaned)
            : null,
        TextFields = TextFields(index),
        FilterFields = FilterFields(index)
    };

    public static IReadOnlyList<SearchTextFieldDto> TextFields(RegisteredIndex index) =>
        index.Definition.TextFields.Select(f => new SearchTextFieldDto(f.Name, f.Weight, f.Analyzer)).ToList();

    public static IReadOnlyList<SearchFilterFieldDto> FilterFields(RegisteredIndex index) =>
        index.Definition.FilterFields.Select(f => new SearchFilterFieldDto(f.Name, f.Type, f.Facetable, f.Sortable)).ToList();
}
