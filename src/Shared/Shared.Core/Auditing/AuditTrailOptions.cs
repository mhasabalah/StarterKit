namespace Shared.Core.Auditing;

/// <summary>Configuration section <c>AuditTrail</c>.</summary>
public sealed class AuditTrailOptions
{
    public const string Section = "AuditTrail";

    /// <summary>A longer string value is cut to this many characters (with an ellipsis).</summary>
    public int MaxValueLength { get; set; } = 512;

    /// <summary>Cap of one entry's <c>ChangesJson</c> in UTF-8 bytes; larger diffs are stored as a list of property names.</summary>
    public int MaxChangesBytes { get; set; } = 16 * 1024;

    /// <summary>Entries older than this are removed by the retention job.</summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>Rows deleted per statement by the retention job.</summary>
    public int PurgeBatchSize { get; set; } = 5000;
}
