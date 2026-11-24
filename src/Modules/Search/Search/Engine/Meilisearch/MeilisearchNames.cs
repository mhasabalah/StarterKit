namespace Search.Engine.Meilisearch;

/// <summary>
/// How the engine-neutral model is named inside Meilisearch. One index per index key, shared by every tenant; each
/// document carries its tenant, branch, version and rebuild stamp in reserved <c>_</c> attributes, its text fields as
/// <c>t_&lt;field&gt;</c> and its filter fields as <c>f_&lt;field&gt;</c>, so an owner's field name can never collide with
/// the adapter's own attributes.
/// </summary>
internal static class MeilisearchNames
{
    /// <summary>The primary key: <c>&lt;tenant&gt;_&lt;id&gt;</c> (both already limited to letters, digits, '-' and '_').</summary>
    public const string Key = "_key";
    public const string Id = "_id";
    public const string Tenant = "_tenant";
    public const string Branch = "_branch";
    public const string DocumentVersion = "_version";
    public const string Stamp = "_stamp";
    public const string IndexedAt = "_indexed_at";
    public const string Highlight = "_highlight";
    public const string RankingScore = "_rankingScore";

    public static string TextAttribute(string field) => "t_" + field;

    public static string FilterAttribute(string field) => "f_" + field;

    public static string TenantValue(Guid tenantId) => tenantId.ToString("N");

    public static string DocumentKey(Guid tenantId, string id) => $"{TenantValue(tenantId)}_{id}";

    /// <summary>
    /// The index uid of <paramref name="indexKey"/>: Meilisearch allows letters, digits, '-' and '_', so '.' becomes
    /// <c>--</c> and '-' becomes <c>-_</c>. Every '-' of the result starts one of the two pairs, so two keys never share
    /// a uid (<c>catalog.products</c> → <c>catalog--products</c>).
    /// </summary>
    public static string IndexUid(string prefix, string indexKey) =>
        prefix + indexKey.Replace("-", "-_", StringComparison.Ordinal).Replace(".", "--", StringComparison.Ordinal);
}
