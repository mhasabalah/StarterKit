using System.Globalization;
using System.Text;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace Notification.Channels.Push;

/// <summary>
/// Maps FCM errors to what the push job does with the device. Pure, so the table is unit-tested.
/// <para>
/// Only <c>UNREGISTERED</c> and <c>SENDER_ID_MISMATCH</c> prove the TOKEN is dead. <c>INVALID_ARGUMENT</c> is
/// ambiguous (a malformed token OR a malformed message): deleting on it would wipe every device of every user after one
/// bad payload, so it is <see cref="PushSendOutcome.Rejected"/> (kept, not retried). Throttling and outages retry.
/// </para>
/// </summary>
public static class FcmErrorClassifier
{
    public static PushSendOutcome Classify(MessagingErrorCode? messagingCode, ErrorCode? errorCode) =>
        messagingCode switch
        {
            MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch => PushSendOutcome.InvalidToken,
            MessagingErrorCode.Unavailable or MessagingErrorCode.Internal or MessagingErrorCode.QuotaExceeded => PushSendOutcome.Transient,
            MessagingErrorCode.InvalidArgument or MessagingErrorCode.ThirdPartyAuthError => PushSendOutcome.Rejected,
            _ => errorCode switch
            {
                ErrorCode.Unavailable or ErrorCode.Internal or ErrorCode.ResourceExhausted or ErrorCode.DeadlineExceeded
                    or ErrorCode.Aborted or ErrorCode.Unknown => PushSendOutcome.Transient,
                ErrorCode.NotFound => PushSendOutcome.InvalidToken,
                _ => PushSendOutcome.Rejected
            }
        };
}

/// <summary>
/// Firebase Cloud Messaging (HTTP v1 through the Admin SDK). One provider for every platform: Android natively, iOS
/// through the APNs key uploaded to the Firebase project, Web through FCM web push.
/// <list type="bullet">
/// <item>The credential is restricted to a SERVICE ACCOUNT key (no external-account JSON, which could make the SDK
/// call arbitrary token URLs).</item>
/// <item>Sends in chunks of 500 (the FCM multicast limit); a chunk-level failure (network, auth) marks that chunk
/// transient so the job retries it - the job never re-sends devices that succeeded.</item>
/// <item>The correlation id is the collapse key on every platform, so duplicates replace instead of stacking.</item>
/// </list>
/// </summary>
public sealed class FcmPushProvider : IPushProvider, IDisposable
{
    private const int MulticastLimit = 500;
    private readonly Lazy<FirebaseApp> _app;
    private readonly ILogger<FcmPushProvider> _logger;

    public FcmPushProvider(IOptions<PushOptions> options, ILogger<FcmPushProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        var opts = options.Value;
        _app = new Lazy<FirebaseApp>(() => CreateApp(opts), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Name => "Fcm";

    public async Task<IReadOnlyList<PushSendResult>> SendAsync(PushMessage message, IReadOnlyList<PushTarget> targets, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(targets);
        var results = new List<PushSendResult>(targets.Count);
        var messaging = FirebaseMessaging.GetMessaging(_app.Value);

        foreach (var chunk in targets.Chunk(MulticastLimit))
        {
            try
            {
                var response = await messaging.SendEachForMulticastAsync(Build(message, chunk), ct);
                for (var i = 0; i < chunk.Length; i++)
                {
                    var r = response.Responses[i];
                    results.Add(r.IsSuccess
                        ? new PushSendResult(chunk[i].DeviceId, PushSendOutcome.Delivered)
                        : new PushSendResult(chunk[i].DeviceId,
                            FcmErrorClassifier.Classify(r.Exception?.MessagingErrorCode, r.Exception?.ErrorCode),
                            Describe(r.Exception)));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Whole request failed (network, OAuth token fetch, 5xx): nothing in this chunk was accepted.
                var outcome = ex is FirebaseMessagingException fme
                    ? FcmErrorClassifier.Classify(fme.MessagingErrorCode, fme.ErrorCode)
                    : PushSendOutcome.Transient;
                if (outcome == PushSendOutcome.InvalidToken)
                    outcome = PushSendOutcome.Transient; // a request-level error never proves a token dead
                _logger.LogWarning(ex, "FCM multicast of {Count} device(s) failed ({Outcome})", chunk.Length, outcome);
                results.AddRange(chunk.Select(t => new PushSendResult(t.DeviceId, outcome, Describe(ex))));
            }
        }

        return results;
    }

    internal static MulticastMessage Build(PushMessage message, IReadOnlyList<PushTarget> targets)
    {
        var ttlSeconds = ((long)message.TimeToLive.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        return new MulticastMessage
        {
            // FirebaseAdmin 3.6 marks Tokens obsolete in favour of Fids (Firebase Installation IDs), but those are a
            // different identifier: devices register FCM registration tokens, which the v1 API still accepts.
            // Moving to Fids needs the clients to send FIDs first, so keep Tokens until then.
#pragma warning disable CS0618 // registration tokens are still the device identifier we store
            Tokens = targets.Select(t => t.Token).ToList(),
#pragma warning restore CS0618
            Notification = new FirebaseAdmin.Messaging.Notification { Title = message.Title, Body = message.Body },
            Data = new Dictionary<string, string>(message.Data, StringComparer.Ordinal),
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                TimeToLive = message.TimeToLive,
                CollapseKey = message.CollapseKey,
                Notification = new AndroidNotification { Tag = message.CollapseKey }
            },
            Apns = new ApnsConfig
            {
                Headers = new Dictionary<string, string>
                {
                    ["apns-collapse-id"] = message.CollapseKey,
                    ["apns-priority"] = "10",
                    ["apns-expiration"] = DateTimeOffset.UtcNow.Add(message.TimeToLive).ToUnixTimeSeconds()
                        .ToString(CultureInfo.InvariantCulture)
                },
                Aps = new Aps { Sound = "default", ThreadId = message.Data.TryGetValue("category", out var category) ? category : null }
            },
            Webpush = new WebpushConfig
            {
                Headers = new Dictionary<string, string> { ["TTL"] = ttlSeconds, ["Urgency"] = "high" },
                Notification = new WebpushNotification { Tag = message.CollapseKey, Renotify = false }
            }
        };
    }

    private static string Describe(Exception? ex) => ex switch
    {
        null => "unknown error",
        FirebaseMessagingException fme => $"{fme.MessagingErrorCode?.ToString() ?? fme.ErrorCode.ToString()}: {fme.Message}",
        _ => $"{ex.GetType().Name}: {ex.Message}"
    };

    private static FirebaseApp CreateApp(PushOptions options)
    {
        var json = !string.IsNullOrWhiteSpace(options.CredentialsJson)
            ? options.CredentialsJson
            : !string.IsNullOrWhiteSpace(options.CredentialsFile)
                ? File.ReadAllText(options.CredentialsFile)
                : throw new InvalidOperationException("Push:Provider is Fcm but no service-account credentials are configured.");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var credential = GoogleCredential.FromServiceAccountCredential(ServiceAccountCredential.FromServiceAccountData(stream));

        // A named app: never collides with a default app another library may create.
        return FirebaseApp.Create(
            new FirebaseAdmin.AppOptions { Credential = credential, ProjectId = string.IsNullOrWhiteSpace(options.ProjectId) ? null : options.ProjectId },
            $"starterkit-notification-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (_app.IsValueCreated)
            _app.Value.Delete();
    }
}
