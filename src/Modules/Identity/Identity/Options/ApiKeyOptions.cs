namespace Identity.Options;

/// <summary>API key limits (<c>Identity:ApiKeys</c>).</summary>
public sealed class ApiKeyOptions
{
    public const string SectionName = "Identity:ApiKeys";

    /// <summary>Master switch. Off = the ApiKey scheme never authenticates and creation is refused.</summary>
    public bool Enabled { get; set; } = true;

    public int DefaultExpiryDays { get; set; } = 90;

    public int MaxExpiryDays { get; set; } = 365;

    /// <summary>Active (not revoked, not expired) keys per user.</summary>
    public int MaxActiveKeysPerUser { get; set; } = 10;

    /// <summary><c>last_used_at</c> is written at most this often per key (keeps the hot path read-only).</summary>
    public int LastUsedWriteIntervalMinutes { get; set; } = 5;
}
