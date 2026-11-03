using System.ComponentModel.DataAnnotations;

namespace Shared.Core.Messaging.Outbox;

/// <summary>Transactional outbox settings (<c>Messaging:Outbox</c>).</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Messaging:Outbox";

    /// <summary>Rows claimed per relay round.</summary>
    [Range(1, 1000)]
    public int BatchSize { get; set; } = 100;

    /// <summary>Publish attempts before a row is dead-lettered (<c>dead_lettered_at</c> set, never retried again).</summary>
    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 10;

    /// <summary>How long a relay owns claimed rows. Longer than a batch publish ever takes.</summary>
    [Range(5, 3600)]
    public int LeaseSeconds { get; set; } = 60;

    /// <summary>Hangfire cron of the safety-net sweep (rows whose immediate delivery failed or never ran).</summary>
    [Required]
    public string SweepCron { get; set; } = "* * * * *";

    /// <summary>Delivered rows are deleted after this many days. Dead-lettered rows are kept until handled.</summary>
    [Range(1, 365)]
    public int RetentionDays { get; set; } = 7;

    /// <summary>Back-off before retry <paramref name="attempt"/> (1-based): 5s, 10s, 20s ... capped at 1 hour.</summary>
    public static TimeSpan BackoffFor(int attempt) =>
        TimeSpan.FromSeconds(Math.Min(3600, 5 * Math.Pow(2, Math.Clamp(attempt, 1, 20) - 1)));
}
