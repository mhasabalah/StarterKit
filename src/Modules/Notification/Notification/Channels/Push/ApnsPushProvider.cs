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

        // 410: the token is no longer active for the topic. 400 BadDeviceToken: malformed / wrong-environment token.
        if (status == HttpStatusCode.Gone || reason is "Unregistered" or "BadDeviceToken")
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
    private string? _jwt;
    private DateTimeOffset _jwtIssuedAt;

    public ApnsPushProvider(HttpClient http, IOptions<PushOptions> options, TimeProvider time, ILogger<ApnsPushProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _time = time;
        _logger = logger;
        _options = options.Value.Apns;
        _key = new Lazy<ECDsa>(() => LoadKey(_options), LazyThreadSafetyMode.ExecutionAndPublication);
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
            request.Headers.Authorization = new AuthenticationHeaderValue("bearer", GetProviderToken());
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
                DropProviderToken();

            return new PushSendResult(target.DeviceId, outcome, $"APNs {(int)response.StatusCode} {reason}".TrimEnd());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            _logger.LogWarning(ex, "APNs request for device {DeviceId} failed", target.DeviceId);
            return new PushSendResult(target.DeviceId, PushSendOutcome.Transient, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static byte[] BuildPayload(PushMessage message)
    {
        var root = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in message.Data)
        {
            if (!string.Equals(key, "aps", StringComparison.Ordinal))
                root[key] = value;
        }

        var aps = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["alert"] = new Dictionary<string, string> { ["title"] = message.Title, ["body"] = message.Body },
            ["sound"] = "default"
        };
        if (message.Data.TryGetValue("category", out var category) && !string.IsNullOrWhiteSpace(category))
            aps["thread-id"] = category;

        root["aps"] = aps;
        return JsonSerializer.SerializeToUtf8Bytes(root);
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
            return _jwt;
        }
    }

    private void DropProviderToken()
    {
        lock (_tokenLock)
            _jwt = null;
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

    private static ECDsa LoadKey(ApnsOptions options)
    {
        var pem = !string.IsNullOrWhiteSpace(options.PrivateKey)
            ? options.PrivateKey
            : !string.IsNullOrWhiteSpace(options.PrivateKeyFile)
                ? File.ReadAllText(options.PrivateKeyFile)
                : throw new InvalidOperationException("Push:Apns is enabled but no .p8 key is configured.");

        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
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

        var apnsTask = toApns.Count > 0 ? apns.SendAsync(message, toApns, ct) : Task.FromResult<IReadOnlyList<PushSendResult>>([]);
        var restTask = rest.Count > 0 ? fallback.SendAsync(message, rest, ct) : Task.FromResult<IReadOnlyList<PushSendResult>>([]);
        await Task.WhenAll(apnsTask, restTask);

        var byDevice = (await apnsTask).Concat(await restTask).ToDictionary(r => r.DeviceId);
        return targets.Select(t => byDevice[t.DeviceId]).ToList();
    }

    /// <summary>A native APNs token is 64+ hexadecimal characters; an FCM registration token never is.</summary>
    public static bool UsesApns(PushTarget target) =>
        target.Platform == PushPlatform.Ios && target.Token.Length >= 64 && target.Token.All(Uri.IsHexDigit);
}
