using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Notification.Channels.Push;

/// <summary>Maps an APNs HTTP status + <c>reason</c> to what the push job does with the device. Pure, unit-tested.</summary>
public static class ApnsErrorClassifier
{
    public static PushSendOutcome Classify(HttpStatusCode status, string? reason)
    {
        if (status == HttpStatusCode.OK)
            return PushSendOutcome.Delivered;

        // Only 410 Unregistered proves the token is dead. 400 BadDeviceToken is ambiguous (a token from the other APNs
        // environment, e.g. a sandbox build against production, looks the same), so deleting on it could wipe every
        // device after one misconfiguration: it is Rejected (device kept, warning logged), never retried.
        if (status == HttpStatusCode.Gone || reason == "Unregistered")
            return PushSendOutcome.InvalidToken;

        // An expired provider token is renewed on the next send; throttling and APNs outages clear on their own.
        if (reason is "ExpiredProviderToken" or "TooManyProviderTokenUpdates" or "TooManyRequests"
            or "InternalServerError" or "ServiceUnavailable" or "Shutdown")
            return PushSendOutcome.Transient;

        if (status == HttpStatusCode.TooManyRequests || (int)status >= 500)
            return PushSendOutcome.Transient;

        // Everything else (bad payload, DeviceTokenNotForTopic, InvalidProviderToken, BadCertificate...) is a
        // configuration or payload problem a retry cannot fix; the device is kept.
        return PushSendOutcome.Rejected;
    }
}

/// <summary>
/// Direct Apple Push Notification service adapter (token-based auth, HTTP/2) for iOS devices that registered a NATIVE
/// APNs device token. No NuGet package: an ES256 JWT signed with the <c>.p8</c> key by <see cref="ECDsa"/>, cached
/// about 50 minutes (APNs refuses tokens older than 60 minutes and more than one new token every 20 minutes).
/// <list type="bullet">
/// <item>The correlation id is <c>apns-collapse-id</c>, so a duplicate delivery replaces the shown notification.</item>
/// <item>The time-to-live is <c>apns-expiration</c>.</item>
/// <item>Sends run with bounded parallelism; per-device failures never throw.</item>
/// </list>
/// </summary>
public sealed class ApnsPushProvider : IPushProvider, IDisposable
{
    public const string ProductionHost = "https://api.push.apple.com";
    public const string SandboxHost = "https://api.sandbox.push.apple.com";
    private const int MaxParallelSends = 16;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(50);

    private readonly HttpClient _http;
    private readonly ApnsOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ApnsPushProvider> _logger;
    private readonly Lazy<ECDsa> _key;
    private readonly object _tokenLock = new();
    private int _mints;
    private string? _jwt;
    private DateTimeOffset _jwtIssuedAt;

    public ApnsPushProvider(HttpClient http, IOptions<PushOptions> options, TimeProvider time, ILogger<ApnsPushProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = time;
        _logger = logger;
        _options = options.Value.Apns;
        // PublicationOnly: a failed load (file briefly unreadable) is retried by the next send, not cached forever.
        _key = new Lazy<ECDsa>(_options.LoadKey, LazyThreadSafetyMode.PublicationOnly);
    }

    public string Name => "Apns";

    public async Task<IReadOnlyList<PushSendResult>> SendAsync(PushMessage message, IReadOnlyList<PushTarget> targets, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(targets);

        var payload = BuildPayload(message);
        using var gate = new SemaphoreSlim(MaxParallelSends);
        var tasks = targets.Select(async target =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await SendOneAsync(message, payload, target, ct);
            }
            finally
            {
                gate.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    private async Task<PushSendResult> SendOneAsync(PushMessage message, byte[] payload, PushTarget target, CancellationToken ct)
    {
        try
        {
            var host = _options.UseSandbox ? SandboxHost : ProductionHost;
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/3/device/{Uri.EscapeDataString(target.Token)}")
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                Content = new ByteArrayContent(payload)
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var jwt = GetProviderToken();
            request.Headers.Authorization = new AuthenticationHeaderValue("bearer", jwt);
            request.Headers.TryAddWithoutValidation("apns-topic", _options.BundleId);
            request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
            request.Headers.TryAddWithoutValidation("apns-priority", "10");
            request.Headers.TryAddWithoutValidation("apns-collapse-id", message.CollapseKey);
            request.Headers.TryAddWithoutValidation("apns-expiration",
                _time.GetUtcNow().Add(message.TimeToLive).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));

            using var response = await _http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.OK)
                return new PushSendResult(target.DeviceId, PushSendOutcome.Delivered);

            var reason = await ReadReasonAsync(response, ct);
            var outcome = ApnsErrorClassifier.Classify(response.StatusCode, reason);
            if (reason == "ExpiredProviderToken")
                DropProviderToken(jwt);
            else if (reason == "BadDeviceToken")
                _logger.LogWarning("APNs rejected the token of device {DeviceId} as BadDeviceToken: check Push:Apns:UseSandbox matches the app build", target.DeviceId);

            return new PushSendResult(target.DeviceId, outcome, $"APNs {(int)response.StatusCode} {reason}".TrimEnd());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "APNs request for device {DeviceId} failed", target.DeviceId);
            return new PushSendResult(target.DeviceId, PushSendOutcome.Transient, $"{ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Key unreadable / unparsable or any other local failure: never throw for one device (the job would drop
            // the results of the others). A configuration problem a retry cannot fix.
            _logger.LogError(ex, "APNs send for device {DeviceId} failed locally", target.DeviceId);
            return new PushSendResult(target.DeviceId, PushSendOutcome.Rejected, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The APNs payload limit for alert notifications.</summary>
    internal const int MaxPayloadBytes = 4096;

    private static readonly JsonSerializerOptions PayloadJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The JSON payload, never over <see cref="MaxPayloadBytes"/> BYTES (non-ASCII text is written as UTF-8, not
    /// \uXXXX escapes). Too large: the body is cut first, then the title, then data entries (largest first).
    /// </summary>
    internal static byte[] BuildPayload(PushMessage message)
    {
        var title = message.Title;
        var body = message.Body;
        var data = message.Data.Where(kv => !string.Equals(kv.Key, "aps", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var bytes = Serialize(title, body, data, message.Data);
        if (bytes.Length <= MaxPayloadBytes)
            return bytes;

        body = Fit(body, text => Serialize(title, text, data, message.Data).Length);
        bytes = Serialize(title, body, data, message.Data);
        if (bytes.Length > MaxPayloadBytes)
        {
            title = Fit(title, text => Serialize(text, body, data, message.Data).Length);
            bytes = Serialize(title, body, data, message.Data);
        }

        while (bytes.Length > MaxPayloadBytes && data.Count > 0)
        {
            data.Remove(data.OrderByDescending(kv => kv.Value.Length).First().Key);
            bytes = Serialize(title, body, data, message.Data);
        }

        return bytes;
    }

    private static byte[] Serialize(string title, string body, Dictionary<string, string> data, IReadOnlyDictionary<string, string> original)
    {
        var root = new Dictionary<string, object>(data.ToDictionary(kv => kv.Key, kv => (object)kv.Value), StringComparer.Ordinal);
        var aps = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["alert"] = new Dictionary<string, string> { ["title"] = title, ["body"] = body },
            ["sound"] = "default"
        };
        if (original.TryGetValue("category", out var category) && !string.IsNullOrWhiteSpace(category))
            aps["thread-id"] = category;

        root["aps"] = aps;
        return JsonSerializer.SerializeToUtf8Bytes(root, PayloadJson);
    }

    /// <summary>The longest prefix of <paramref name="text"/> (plus an ellipsis) whose payload fits, never splitting a surrogate pair.</summary>
    private static string Fit(string text, Func<string, int> sizeWith)
    {
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (sizeWith(Cut(text, mid)) <= MaxPayloadBytes)
                lo = mid;
            else
                hi = mid - 1;
        }

        return Cut(text, lo);

        static string Cut(string value, int length)
        {
            if (length >= value.Length)
                return value;
            if (length > 0 && char.IsHighSurrogate(value[length - 1]))
                length--;
            return value[..length] + "\u2026";
        }
    }

    private static async Task<string?> ReadReasonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>How many provider tokens were signed (tests assert a burst of expiries re-mints once).</summary>
    internal int TokenMints => Volatile.Read(ref _mints);

    /// <summary>The cached provider token, minted again after about 50 minutes (thread-safe).</summary>
    internal string GetProviderToken()
    {
        lock (_tokenLock)
        {
            var now = _time.GetUtcNow();
            if (_jwt is not null && now - _jwtIssuedAt < TokenLifetime)
                return _jwt;

            _jwt = CreateJwt(_key.Value, _options.KeyId, _options.TeamId, now);
            _jwtIssuedAt = now;
            Interlocked.Increment(ref _mints);
            return _jwt;
        }
    }

    /// <summary>
    /// Forgets the cached token, but only if it is still <paramref name="used"/>: a late ExpiredProviderToken answer to
    /// an old request must not throw away the fresh token another request already minted.
    /// </summary>
    private void DropProviderToken(string used)
    {
        lock (_tokenLock)
        {
            if (string.Equals(_jwt, used, StringComparison.Ordinal))
                _jwt = null;
        }
    }

    internal static string CreateJwt(ECDsa key, string keyId, string teamId, DateTimeOffset issuedAt)
    {
        var header = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["alg"] = "ES256", ["kid"] = keyId });
        var claims = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["iss"] = teamId, ["iat"] = issuedAt.ToUnixTimeSeconds() });
        var signingInput = $"{Base64Url.EncodeToString(header)}.{Base64Url.EncodeToString(claims)}";
        var signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    public void Dispose()
    {
        if (_key.IsValueCreated)
            _key.Value.Dispose();
    }
}

/// <summary>
/// Sends each device through the provider that owns it. With APNs configured, iOS devices holding a native APNs token
/// (hexadecimal) go straight to Apple; iOS devices holding an FCM token (the Firebase SDK's token) and every other
/// platform go through the fallback (FCM, or NoOp). Without APNs everything goes to the fallback.
/// </summary>
public sealed class RoutingPushProvider(IPushProvider fallback, IPushProvider? apns) : IPushProvider
{
    public string Name => apns is null ? fallback.Name : $"{fallback.Name}+{apns.Name}";

    public async Task<IReadOnlyList<PushSendResult>> SendAsync(PushMessage message, IReadOnlyList<PushTarget> targets, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (apns is null)
            return await fallback.SendAsync(message, targets, ct);

        var toApns = targets.Where(UsesApns).ToList();
        var rest = targets.Where(t => !UsesApns(t)).ToList();

        // Each branch is isolated: one provider failing must not discard the other's results.
        var apnsTask = toApns.Count > 0 ? SafeSendAsync(apns, message, toApns, ct) : Task.FromResult<IReadOnlyList<PushSendResult>>([]);
        var restTask = rest.Count > 0 ? SafeSendAsync(fallback, message, rest, ct) : Task.FromResult<IReadOnlyList<PushSendResult>>([]);
        await Task.WhenAll(apnsTask, restTask);

        var byDevice = (await apnsTask).Concat(await restTask).ToDictionary(r => r.DeviceId);
        return targets.Select(t => byDevice.TryGetValue(t.DeviceId, out var r)
            ? r
            : new PushSendResult(t.DeviceId, PushSendOutcome.Transient, "The push provider returned no result for this device.")).ToList();
    }

    private static async Task<IReadOnlyList<PushSendResult>> SafeSendAsync(
        IPushProvider provider, PushMessage message, IReadOnlyList<PushTarget> targets, CancellationToken ct)
    {
        try
        {
            return await provider.SendAsync(message, targets, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return targets.Select(t => new PushSendResult(t.DeviceId, PushSendOutcome.Transient,
                $"{provider.Name} failed: {ex.GetType().Name}: {ex.Message}")).ToList();
        }
    }

    /// <summary>A native APNs token is 64+ hexadecimal characters; an FCM registration token never is.</summary>
    public static bool UsesApns(PushTarget target) =>
        target.Platform == PushPlatform.Ios && target.Token.Length >= 64 && target.Token.All(Uri.IsHexDigit);
}
