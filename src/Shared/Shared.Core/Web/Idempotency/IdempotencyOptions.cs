namespace Shared.Core.Web.Idempotency;

/// <summary>Configuration section <c>Idempotency</c> (endpoints opt in with <c>.WithIdempotency()</c>).</summary>
public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>How long a completed response can be replayed (default 24 h).</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long a reservation blocks duplicates while the first request is still running. If the process dies the
    /// key frees itself after this time.
    /// </summary>
    public TimeSpan InFlightTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Responses with a larger body are returned but not stored (the key is released).</summary>
    public int MaxStoredBodyBytes { get; set; } = 256 * 1024;

    /// <summary>Longest accepted <c>Idempotency-Key</c> header value.</summary>
    public int MaxKeyLength { get; set; } = 255;
}
