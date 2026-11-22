using Agentic.Contracts.Knowledge;
using Agentic.Runtime.Knowledge;
using Agentic.Runtime.Security;
using Microsoft.Extensions.Options;

namespace Agentic.Domains.Knowledge.Features;

/// <summary>A passage returned to an agent, with what it needs to cite it.</summary>
/// <param name="Citation"><c>&lt;source&gt;/&lt;document&gt;#&lt;passage&gt;</c>: stable, and resolvable by the owning module.</param>
public sealed record KnowledgePassage(
    string Citation,
    string Source,
    string DocumentRef,
    string Title,
    int Passage,
    string Text,
    double Score);

/// <summary>
/// Hybrid search for the tool <c>knowledge.search</c>. Internal to the module on purpose: it answers only inside an
/// executing tool call, because what it may return depends on the run's EFFECTIVE permissions
/// (<see cref="AgentCallScope"/>), not on whoever sends a query.
/// </summary>
public sealed record SearchKnowledgeQuery(string Query, IReadOnlyList<string>? Sources, int Top)
    : IQuery<IReadOnlyList<KnowledgePassage>>;

/// <summary>
/// Searches the tenant's knowledge for the user a run acts for. Access is decided in code, in this order, and the
/// model's words take no part in it:
/// <list type="number">
/// <item>Sources: only those whose permission the run holds now (the initiator's permissions narrowed by an API-key
/// ceiling); the sources the model asked for can only narrow that.</item>
/// <item>Tenant: SQL names the tenant and runs under row-level security.</item>
/// <item>Documents: each source's <see cref="IKnowledgeSourcePolicy"/> filters the hits, as the initiator.</item>
/// </list>
/// Without an embedding (provider down) the search degrades to keywords only instead of failing the call.
/// </summary>
public class SearchKnowledgeHandler(
    AgenticDbContext dbContext,
    AgentCallScope call,
    KnowledgeSourceRegistry sources,
    IKnowledgeEmbedder embedder,
    IEnumerable<IKnowledgeSourcePolicy> policies,
    ITenantContext tenantContext,
    IOptions<AgenticOptions> options,
    ILogger<SearchKnowledgeHandler> logger) : IQueryHandler<SearchKnowledgeQuery, IReadOnlyList<KnowledgePassage>>
{
    public async Task<IReadOnlyList<KnowledgePassage>> Handle(SearchKnowledgeQuery query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.Knowledge.Enabled)
            throw new KnowledgeUnavailableException();
        var principal = call.Principal
                        ?? throw new ForbiddenException(AgenticText.Get("Agent.Knowledge.run_only"));
        var tenantId = tenantContext.TenantId
                       ?? throw new ForbiddenException(AgenticText.Get("Agent.Knowledge.run_only"));

        IEnumerable<string> readable = sources.ReadableBy(principal.Permissions);
        if (query.Sources is { Count: > 0 } requested)
            readable = readable.Intersect(requested, StringComparer.Ordinal);
        var allowed = readable.ToList();
        if (allowed.Count == 0)
            return [];

        float[]? vector = null;
        try
        {
            vector = (await embedder.EmbedAsync([query.Query], cancellationToken))[0];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Knowledge search of run {RunId}: no query embedding, keyword search only", call.RunId);
        }

        var top = Math.Clamp(query.Top, 1, settings.Knowledge.MaxResults);
        var hits = await KnowledgeStore.SearchAsync(dbContext, tenantId, query.Query, vector, embedder.ModelIdentity,
            allowed, settings.Knowledge.SearchCandidates, top * 3, cancellationToken, KnowledgeVectorIndex.From(settings));
        hits = await FilterByDocumentPolicyAsync(hits, cancellationToken);

        return hits.Take(top)
            .Select(h => new KnowledgePassage($"{h.SourceKey}/{h.ExternalRef}#{h.Ordinal}", h.SourceKey, h.ExternalRef,
                h.Title, h.Ordinal, h.Content, Math.Round(h.Score, 5)))
            .ToList();
    }

    private async Task<IReadOnlyList<KnowledgeHit>> FilterByDocumentPolicyAsync(IReadOnlyList<KnowledgeHit> hits,
        CancellationToken cancellationToken)
    {
        var bySource = policies.GroupBy(p => p.SourceKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList());
        if (bySource.Count == 0)
            return hits;

        var denied = new HashSet<(string Source, string Ref)>();
        foreach (var group in hits.GroupBy(h => h.SourceKey, StringComparer.Ordinal))
        {
            if (!bySource.TryGetValue(group.Key, out var guards))
                continue;

            var refs = group.Select(h => h.ExternalRef).Distinct(StringComparer.Ordinal).ToList();
            IEnumerable<string> allowed = refs;
            foreach (var guard in guards)
            {
                var readable = await guard.GetReadableAsync(refs, cancellationToken);
                allowed = allowed.Where(readable.Contains).ToList();
            }

            var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
            foreach (var reference in refs.Where(r => !allowedSet.Contains(r)))
                denied.Add((group.Key, reference));
        }

        return denied.Count == 0 ? hits : hits.Where(h => !denied.Contains((h.SourceKey, h.ExternalRef))).ToList();
    }
}
