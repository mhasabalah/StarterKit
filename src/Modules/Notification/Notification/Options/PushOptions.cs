namespace Notification.Options;

public enum PushProvider
{
    /// <summary>FCM when credentials are configured, otherwise NoOp (logged at startup). Lets a project without push boot.</summary>
    Auto = 0,

    /// <summary>Firebase Cloud Messaging (HTTP v1). Android, iOS (through the APNs key uploaded to Firebase) and Web.</summary>
    Fcm = 1,

    /// <summary>Logs a masked summary instead of sending (development, tests).</summary>
    NoOp = 2,
}

/// <summary>Bound from <c>Push</c>. The service-account JSON comes from the Docker secret <c>fcm_service_account_json</c>.</summary>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    public PushProvider Provider { get; set; } = PushProvider.Auto;

    /// <summary>Firebase project id. Optional: read from the service-account JSON when empty.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Service-account JSON (secret). Never put it in appsettings; use the Docker secret or an env var.</summary>
    public string? CredentialsJson { get; set; }

    /// <summary>Path of a service-account JSON file (alternative to <see cref="CredentialsJson"/>).</summary>
    public string? CredentialsFile { get; set; }

    /// <summary>How long FCM keeps a message for an offline device when the sender sets none. FCM caps it at 28 days.</summary>
    public TimeSpan DefaultTimeToLive { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Send attempts per device on transient FCM errors (5xx, 429) before the device is given up for this message.</summary>
    public int MaxAttempts { get; set; } = 4;

    /// <summary>Delay before each retry of the transiently failed devices (attempt 2, 3, ...; the last value repeats).</summary>
    public int[] RetryDelaysSeconds { get; set; } = [30, 120, 600];

    /// <summary>Devices kept per user; registering one more evicts the least recently seen.</summary>
    public int MaxDevicesPerUser { get; set; } = 10;

    /// <summary>Devices not re-registered for this many days are purged (FCM treats ~2 months inactive tokens as stale).</summary>
    public int StaleDeviceDays { get; set; } = 60;

    /// <summary>Direct APNs adapter for iOS devices that register a native APNs token (<c>Push:Apns</c>).</summary>
    public ApnsOptions Apns { get; set; } = new();

    public bool HasCredentials => !string.IsNullOrWhiteSpace(CredentialsJson) || !string.IsNullOrWhiteSpace(CredentialsFile);

    /// <summary>Delay before retry number <paramref name="attempt"/> (2-based: the delay before the second attempt is index 0).</summary>
    public TimeSpan RetryDelay(int attempt)
    {
        if (RetryDelaysSeconds is not { Length: > 0 })
            return TimeSpan.FromSeconds(60);
        var index = Math.Clamp(attempt - 2, 0, RetryDelaysSeconds.Length - 1);
        return TimeSpan.FromSeconds(Math.Max(1, RetryDelaysSeconds[index]));
    }
}

/// <summary>
/// Bound from <c>Push:Apns</c>: token-based APNs auth. The <c>.p8</c> key is a secret FILE read by path
/// (<see cref="PrivateKeyFile"/>, like the FCM service-account JSON): it never enters configuration or logs.
/// </summary>
public sealed class ApnsOptions
{
    /// <summary>False (default) = iOS goes through FCM. True needs <see cref="KeyId"/>, <see cref="TeamId"/>, <see cref="BundleId"/> and the key file.</summary>
    public bool Enabled { get; set; }

    /// <summary>The 10-character key id of the APNs auth key (Apple Developer, Keys).</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Your 10-character Apple team id.</summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>The app's bundle id, sent as <c>apns-topic</c>.</summary>
    public string BundleId { get; set; } = string.Empty;

    /// <summary>Path of the <c>.p8</c> key file (PKCS#8 PEM), e.g. <c>/run/secrets/apns_auth_key.p8</c>.</summary>
    public string? PrivateKeyFile { get; set; }

    /// <summary>True = the sandbox host, for development-build tokens. A token only works in the environment that issued it.</summary>
    public bool UseSandbox { get; set; }

    public bool HasKey => !string.IsNullOrWhiteSpace(PrivateKeyFile);

    /// <summary>Reads and parses the key file. Throws when it is missing or is not an EC private key.</summary>
    public System.Security.Cryptography.ECDsa LoadKey()
    {
        var key = System.Security.Cryptography.ECDsa.Create();
        try
        {
            key.ImportFromPem(File.ReadAllText(PrivateKeyFile ?? throw new InvalidOperationException("Push:Apns:PrivateKeyFile is not set.")));
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Null when valid, otherwise what is wrong: missing settings (<c>CHANGE_ME</c> placeholders count as missing) or a
    /// key file that cannot be read or parsed. Checked at startup so a bad key fails the boot, not every push.
    /// </summary>
    public string? Validate()
    {
        static bool Missing(string? v) => string.IsNullOrWhiteSpace(v) || v.Contains("CHANGE_ME", StringComparison.Ordinal);
        if (!Enabled)
            return null;
        if (Missing(KeyId) || Missing(TeamId) || Missing(BundleId))
            return "Push:Apns:Enabled needs KeyId, TeamId and BundleId.";
        if (!HasKey)
            return "Push:Apns:Enabled needs the .p8 key file (Push:Apns:PrivateKeyFile).";

        try
        {
            using var key = LoadKey();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            return $"Push:Apns:PrivateKeyFile cannot be read as an EC private key ({ex.GetType().Name}).";
        }
    }
}

/// <summary>Bound from <c>Inbox</c>.</summary>
public sealed class InboxOptions
{
    public const string SectionName = "Inbox";

    /// <summary>Days an inbox entry is kept (read or not). Default: 90.</summary>
    public int RetentionDays { get; set; } = 90;
}

/// <summary>Bound from <c>Realtime</c>: the SignalR notification hub.</summary>
public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>False = no hub is mapped (the inbox still works over REST; clients poll the unread count).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Allowed transports, comma-separated: <c>WebSockets</c>, <c>ServerSentEvents</c>, <c>LongPolling</c>. A single
    /// string, not a JSON array: configuration merges arrays by index, so an environment file could never REMOVE a
    /// transport from an array. Behind a load balancer WITHOUT sticky sessions use <c>WebSockets</c> only, and clients
    /// connect with <c>skipNegotiation: true</c>.
    /// </summary>
    public string Transports { get; set; } = "WebSockets,ServerSentEvents,LongPolling";

    /// <summary>The transport names of <see cref="Transports"/>.</summary>
    public string[] TransportNames =>
        (Transports ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Server ping interval. Must be well below the proxy read timeout (nginx: 3600s on /hubs/).</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>A client silent this long is dropped. Keep it at about twice the CLIENT's keep-alive.</summary>
    public TimeSpan ClientTimeoutInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Use the Redis backplane when Redis is enabled (required with more than one API replica: a send on replica A
    /// must reach a socket held by replica B).
    /// </summary>
    public bool UseRedisBackplane { get; set; } = true;
}
