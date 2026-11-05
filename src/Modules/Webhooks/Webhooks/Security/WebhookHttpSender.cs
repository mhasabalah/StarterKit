using Shared.Core.Net;
using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Webhooks.Security;

public enum WebhookFailureKind
{
    None,

    /// <summary>Worth another attempt: timeout, connection error, 408/425/429, 5xx.</summary>
    Retriable,

    /// <summary>Retrying cannot help: blocked address, redirect, other 4xx.</summary>
    Permanent
}

public sealed record WebhookRequest(string Url, Guid MessageId, long Timestamp, string Signature, string Body);

public sealed record WebhookSendResult(
    bool Success,
    int? StatusCode,
    int DurationMs,
    string? Error,
    string? ResponseSnippet,
    WebhookFailureKind Failure);

/// <summary>Sends one signed webhook request. Never throws for delivery problems: they come back as a result.</summary>
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Outbound HTTP for webhooks. Deliberately NOT an <c>IHttpClientFactory</c> client: the host adds a standard resilience
/// handler (3 retries) to every factory client, and retries here belong to ONE layer (the Hangfire delivery job, which
/// knows the attempt count and never double-sends). A single long-lived client with a guarded
/// <see cref="SocketsHttpHandler"/> keeps connection pooling and rules out any hidden resend.
/// <list type="bullet">
/// <item>SSRF: <see cref="GuardedConnector"/> resolves DNS and refuses non-public addresses at connect time.</item>
/// <item>No proxy, no redirects (a 3xx is a permanent failure), no cookies, no automatic decompression.</item>
/// <item>Hard timeout; at most <see cref="WebhooksOptions.MaxResponseBytes"/> of the response is read.</item>
/// </list>
/// </summary>
public sealed class WebhookHttpSender : IWebhookSender, IDisposable
{
    private const string UserAgent = "StarterKit-Webhooks/1.0";

    private readonly HttpClient _client;
    private readonly WebhooksOptions _options;

    public WebhookHttpSender(IOptions<WebhooksOptions> options, GuardedConnector? connector = null)
    {
        _options = options.Value;
        connector ??= new GuardedConnector(_options.AllowPrivateNetworks);

        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxResponseHeadersLength = 16, // KB
            ConnectCallback = connector.ConnectAsync
        };

        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan // the per-request linked token below enforces the deadline
        };
    }

    public async Task<WebhookSendResult> SendAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, request.Url)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(request.Body))
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.TryAddWithoutValidation("webhook-id", WebhookSigner.MessageIdHeader(request.MessageId));
            message.Headers.TryAddWithoutValidation("webhook-timestamp", request.Timestamp.ToString(CultureInfo.InvariantCulture));
            message.Headers.TryAddWithoutValidation("webhook-signature", request.Signature);
            message.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var snippet = await ReadCappedAsync(response, timeout.Token);
            var code = (int)response.StatusCode;
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return new WebhookSendResult(true, code, Ms(stopwatch), null, snippet, WebhookFailureKind.None);
            }

            var kind = code is 408 or 425 or 429 or >= 500 ? WebhookFailureKind.Retriable : WebhookFailureKind.Permanent;
            var error = code is >= 300 and < 400
                ? $"HTTP {code}: redirects are not followed"
                : $"HTTP {code}";
            return new WebhookSendResult(false, code, Ms(stopwatch), error, snippet, kind);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(stopwatch, "Request timed out.", WebhookFailureKind.Retriable);
        }
        catch (BlockedNetworkAddressException)
        {
            return Failure(stopwatch, "Blocked: the target resolves to a non-public address.", WebhookFailureKind.Permanent);
        }
        catch (HttpRequestException ex) when (ex.InnerException is BlockedNetworkAddressException)
        {
            return Failure(stopwatch, "Blocked: the target resolves to a non-public address.", WebhookFailureKind.Permanent);
        }
        catch (HttpRequestException ex)
        {
            var reason = ex.InnerException is System.Net.Sockets.SocketException socket
                ? socket.SocketErrorCode.ToString()
                : ex.Message;
            return Failure(stopwatch, $"Connection failed: {reason}", WebhookFailureKind.Retriable);
        }
        catch (InvalidOperationException ex)
        {
            return Failure(stopwatch, $"Invalid request: {ex.Message}", WebhookFailureKind.Permanent);
        }
    }

    private async Task<string?> ReadCappedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var captured = new MemoryStream();
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var limit = Math.Max(1, _options.MaxResponseBytes);
            var buffer = new byte[Math.Min(limit, 8192)];
            var total = 0;
            while (total < limit)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit - total)), ct);
                if (read == 0)
                    break;
                total += read;
                if (captured.Length < 4096)
                    captured.Write(buffer, 0, read);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            // The status line already decided the outcome: a slow or broken BODY (including the deadline hitting while
            // it is read) must not turn a received 2xx into a timeout, or the receiver gets the event twice.
            // Whatever was read so far is kept as the snippet.
        }

        // Anything beyond the cap is simply not read: the connection is dropped when the response is disposed.
        return captured.Length == 0 ? null : Encoding.UTF8.GetString(captured.GetBuffer(), 0, (int)Math.Min(captured.Length, 4096));
    }

    private static WebhookSendResult Failure(Stopwatch stopwatch, string error, WebhookFailureKind kind)
    {
        stopwatch.Stop();
        return new WebhookSendResult(false, null, Ms(stopwatch), error, null, kind);
    }

    private static int Ms(Stopwatch stopwatch) => (int)Math.Min(int.MaxValue, stopwatch.ElapsedMilliseconds);

    public void Dispose() => _client.Dispose();
}
