using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Search.Engine.Meilisearch;

/// <summary>
/// Creates the Meilisearch index of a definition and applies its settings. The reconcile job forces it every run (a
/// changed definition, or an index deleted on the server, is repaired within one interval); reads and writes ensure it
/// once per process and definition fingerprint, so the first write after a deploy never lands in an unconfigured index.
/// </summary>
internal sealed class MeilisearchIndexes(MeilisearchClient client, MeilisearchIndexCache cache, IOptions<SearchOptions> options)
{
    public string Uid(RegisteredIndex index) => MeilisearchNames.IndexUid(options.Value.Meilisearch.IndexPrefix, index.Key);

    public async Task<string> EnsureAsync(RegisteredIndex index, bool force, CancellationToken cancellationToken)
    {
        var uid = Uid(index);
        if (!force && cache.IsCurrent(uid, index.Fingerprint))
            return uid;

        try
        {
            await client.RunTaskAsync(HttpMethod.Post, "indexes",
                new JsonObject { ["uid"] = uid, ["primaryKey"] = MeilisearchNames.Key }, cancellationToken);
        }
        catch (MeilisearchApiException ex) when (ex.Code == MeilisearchApiException.IndexAlreadyExists)
        {
            // Idempotent: the index exists; its settings are applied below.
        }

        await client.RunTaskAsync(HttpMethod.Patch, $"indexes/{uid}/settings",
            MeilisearchIndexSettings.Build(index, options.Value), cancellationToken);
        cache.MarkCurrent(uid, index.Fingerprint);
        return uid;
    }
}

/// <summary>Which index uids this process has configured, and with which definition fingerprint.</summary>
internal sealed class MeilisearchIndexCache
{
    private readonly ConcurrentDictionary<string, string> _current = new(StringComparer.Ordinal);

    public bool IsCurrent(string uid, string fingerprint) =>
        _current.TryGetValue(uid, out var applied) && applied == fingerprint;

    public void MarkCurrent(string uid, string fingerprint) => _current[uid] = fingerprint;
}
