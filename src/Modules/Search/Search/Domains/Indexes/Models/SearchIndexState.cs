namespace Search.Domains.Indexes.Models;

/// <summary>
/// The build state of one index in one tenant: which rebuild stamp is live, who holds the rebuild lease, and the last
/// drift check. The documents themselves live in the engine; this row (always in PostgreSQL) decides whether the index
/// can answer, whatever the engine.
/// </summary>
public class SearchIndexState : Aggregate<Guid>, IMustHaveTenant
{
    public const int IndexKeyMaxLength = SearchIndexDefinition.KeyMaxLength;
    public const int FingerprintLength = 64;
    public const int ErrorCodeMaxLength = 100;

    public Guid TenantId { get; set; }

    public string IndexKey { get; private set; } = default!;

    public SearchIndexStatus Status { get; private set; }

    /// <summary>The fingerprint of the definition the live documents were built with.</summary>
    public string? DefinitionFingerprint { get; private set; }

    /// <summary>The stamp of the last completed rebuild (0 = none): live writes carry it.</summary>
    public long ActiveStamp { get; private set; }

    /// <summary>The stamp of the rebuild in progress.</summary>
    public long? BuildingStamp { get; private set; }

    public DateTime? BuildStartedAt { get; private set; }

    public DateTime? LeaseUntil { get; private set; }

    public DateTime? LastBuiltAt { get; private set; }

    public int DocumentCount { get; private set; }

    public string? LastErrorCode { get; private set; }

    public DateTime? DriftCheckedAt { get; private set; }

    public int DriftMissing { get; private set; }

    public int DriftStale { get; private set; }

    public int DriftOrphaned { get; private set; }

    public new DateTime CreatedAt { get; private set; }

    private SearchIndexState()
    {
    }

    public static SearchIndexState Create(string indexKey, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexKey);
        return new SearchIndexState
        {
            Id = Guid.CreateVersion7(),
            IndexKey = indexKey,
            Status = SearchIndexStatus.NotBuilt,
            CreatedAt = now
        };
    }

    /// <summary>A completed build exists, so searches can be answered (also while a newer build runs).</summary>
    public bool CanAnswer => LastBuiltAt is not null;

    /// <summary>The stamp a live write carries: the rebuild in progress, or the last completed one.</summary>
    public long WriteStamp => BuildingStamp ?? ActiveStamp;

    /// <summary>The live documents were built from another definition (or never built).</summary>
    public bool IsStale(string fingerprint) => !CanAnswer || DefinitionFingerprint != fingerprint;

    /// <summary>Another worker holds an unexpired rebuild lease.</summary>
    public bool IsLeased(DateTime now) => Status == SearchIndexStatus.Building && LeaseUntil > now;

    /// <summary>Takes the rebuild lease, unless another worker holds an unexpired one.</summary>
    public bool TryStartBuild(long stamp, DateTime now, TimeSpan lease)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stamp);
        if (IsLeased(now))
            return false;

        Status = SearchIndexStatus.Building;
        BuildingStamp = stamp;
        BuildStartedAt = now;
        LeaseUntil = now + lease;
        return true;
    }

    /// <summary>Extends the lease of the rebuild <paramref name="stamp"/>; false when another worker took it over.</summary>
    public bool ExtendLease(long stamp, DateTime now, TimeSpan lease)
    {
        if (Status != SearchIndexStatus.Building || BuildingStamp != stamp)
            return false;
        LeaseUntil = now + lease;
        return true;
    }

    /// <summary>Makes the rebuild <paramref name="stamp"/> the live one.</summary>
    public void CompleteBuild(long stamp, int documentCount, string fingerprint, DateTime now)
    {
        EnsureBuilding(stamp);
        ArgumentOutOfRangeException.ThrowIfNegative(documentCount);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        Status = SearchIndexStatus.Ready;
        ActiveStamp = stamp;
        BuildingStamp = null;
        LeaseUntil = null;
        LastBuiltAt = now;
        DocumentCount = documentCount;
        DefinitionFingerprint = fingerprint;
        LastErrorCode = null;
    }

    /// <summary>The rebuild <paramref name="stamp"/> failed: a previous build keeps answering.</summary>
    public void FailBuild(long stamp, string errorCode)
    {
        EnsureBuilding(stamp);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        Status = CanAnswer ? SearchIndexStatus.Ready : SearchIndexStatus.Failed;
        BuildingStamp = null;
        LeaseUntil = null;
        LastErrorCode = errorCode.Length > ErrorCodeMaxLength ? errorCode[..ErrorCodeMaxLength] : errorCode;
    }

    public void RecordDrift(int missing, int stale, int orphaned, DateTime now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(missing);
        ArgumentOutOfRangeException.ThrowIfNegative(stale);
        ArgumentOutOfRangeException.ThrowIfNegative(orphaned);

        DriftMissing = missing;
        DriftStale = stale;
        DriftOrphaned = orphaned;
        DriftCheckedAt = now;
    }

    private void EnsureBuilding(long stamp)
    {
        if (Status != SearchIndexStatus.Building || BuildingStamp != stamp)
        {
            throw new InvalidOperationException(
                $"Search index '{IndexKey}' is not building stamp {stamp} (status {Status}, building {BuildingStamp}).");
        }
    }
}
