namespace Webhooks.Options;

/// <summary>Bound from the <c>"Webhooks"</c> appsettings section.</summary>
public sealed class WebhooksOptions
{
    public const string SectionName = "Webhooks";

    // ----- delivery / retry -------------------------------------------------------------------------------------

    /// <summary>Total attempts per delivery (first try included).</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>Delay before the 2nd attempt; later delays multiply by <see cref="RetryBackoffFactor"/>.</summary>
    public int RetryBaseDelaySeconds { get; set; } = 10;

    public double RetryBackoffFactor { get; set; } = 6;

    public int MaxRetryDelayMinutes { get; set; } = 360;

    /// <summary>An endpoint is auto-disabled (and the tenant owner notified) after this many consecutive failed attempts.</summary>
    public int AutoDisableAfterConsecutiveFailures { get; set; } = 10;

    public int MaxEndpointsPerTenant { get; set; } = 20;

    // ----- outbound HTTP --------------------------------------------------------------------------------------------

    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>Response bytes read at most (the rest is ignored); only a short snippet is stored.</summary>
    public int MaxResponseBytes { get; set; } = 64 * 1024;

    /// <summary>Largest webhook body we send; bigger payloads are not delivered (failed permanently).</summary>
    public int MaxPayloadBytes { get; set; } = 256 * 1024;

    // ----- SSRF guard -------------------------------------------------------------------------------------------------

    /// <summary>Endpoint URLs must be https. Set false only in development / tests (refused in production).</summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// DEVELOPMENT ONLY: lets endpoints target loopback / private / link-local addresses (a local receiver). The SSRF
    /// guard is otherwise always on. Refused at startup outside Development / Testing.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    // ----- jobs -------------------------------------------------------------------------------------------------------

    /// <summary>Recurring sweep that re-enqueues deliveries whose job was lost (crash between commit and enqueue).</summary>
    public string DispatchSweepCron { get; set; } = "* * * * *";

    /// <summary>A pending delivery this overdue is considered lost and is re-enqueued by the sweep.</summary>
    public int SweepStuckAfterMinutes { get; set; } = 5;

    public int SweepBatchSize { get; set; } = 200;

    public int RetentionDays { get; set; } = 30;

    public int PurgeBatchSize { get; set; } = 1000;

    public string PurgeCron { get; set; } = "30 3 * * *";

    /// <summary>Delay before attempt number <paramref name="nextAttempt"/> (2-based), capped.</summary>
    public TimeSpan DelayBeforeAttempt(int nextAttempt)
    {
        var exponent = Math.Max(0, nextAttempt - 2);
        var seconds = RetryBaseDelaySeconds * Math.Pow(Math.Max(1, RetryBackoffFactor), exponent);
        var capped = Math.Min(seconds, MaxRetryDelayMinutes * 60d);
        return TimeSpan.FromSeconds(Math.Max(1, capped));
    }
}
