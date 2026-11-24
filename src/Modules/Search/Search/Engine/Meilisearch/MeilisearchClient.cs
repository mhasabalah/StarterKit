using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Polly;

namespace Search.Engine.Meilisearch;

/// <summary>
/// The HTTP transport to Meilisearch (a typed <see cref="HttpClient"/>: base address, API key, timeouts and circuit
/// breaker are configured at registration). It knows the protocol, not search: JSON in and out, asynchronous tasks
/// awaited to completion, and failures sorted into two kinds:
/// <list type="bullet">
/// <item><see cref="SearchEngineUnavailableException"/>: the server is down, slow, overloaded or the circuit is open.
/// Searches fall back to SQL; indexing is retried by its consumer or job.</item>
/// <item><see cref="MeilisearchApiException"/>: Meilisearch refused the request (a bug or a configuration error); never
/// retried, never hidden.</item>
/// </list>
/// </summary>
internal sealed class MeilisearchClient(HttpClient http, IOptions<SearchOptions> options, TimeProvider time)
{
    public const string EngineName = "Meilisearch";

    /// <summary>
    /// Request bodies keep non-ASCII text as is (an Arabic name is 2 bytes a letter instead of a 6-byte escape). Safe:
    /// the bodies go to an API, never into HTML.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly TimeSpan FirstPoll = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MaxPoll = TimeSpan.FromMilliseconds(250);

    public async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(Json), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (IsUnreachable(ex, cancellationToken))
        {
            throw new SearchEngineUnavailableException(EngineName, ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
                return string.IsNullOrWhiteSpace(payload) ? null : JsonNode.Parse(payload);

            var error = MeilisearchApiException.From(response.StatusCode, payload);
            throw IsTransient(response.StatusCode) ? new SearchEngineUnavailableException(EngineName, error) : error;
        }
    }

    /// <summary>Sends a request that enqueues a task and waits for it; returns the finished task.</summary>
    public async Task<JsonNode> RunTaskAsync(HttpMethod method, string path, JsonNode? body, CancellationToken cancellationToken)
    {
        var enqueued = await SendAsync(method, path, body, cancellationToken);
        var uid = enqueued?["taskUid"]?.GetValue<long>()
                  ?? throw new InvalidOperationException($"Meilisearch did not return a task for {method} {path}.");
        return await WaitForTaskAsync(uid, cancellationToken);
    }

    /// <summary>
    /// Polls the task until it finishes (back-off 10 ms → 250 ms). Writes are only acknowledged once applied, so a
    /// successful indexing call means the documents are searchable. Longer than <c>Meilisearch:TaskTimeoutSeconds</c>
    /// is an unavailable engine (the task keeps running; the caller's retry finds it applied or applies it again).
    /// </summary>
    public async Task<JsonNode> WaitForTaskAsync(long taskUid, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var timeout = TimeSpan.FromSeconds(options.Value.Meilisearch.TaskTimeoutSeconds);
        var delay = FirstPoll;

        while (true)
        {
            var task = await SendAsync(HttpMethod.Get, $"tasks/{taskUid}", null, cancellationToken)
                       ?? throw new InvalidOperationException($"Meilisearch returned no task {taskUid}.");

            switch (task["status"]?.GetValue<string>())
            {
                case "succeeded":
                    return task;
                case "failed":
                    throw MeilisearchApiException.FromTask(task);
                case "canceled":
                    throw new InvalidOperationException($"Meilisearch task {taskUid} was canceled.");
            }

            if (time.GetElapsedTime(started) > timeout)
            {
                throw new SearchEngineUnavailableException(EngineName,
                    new TimeoutException($"Meilisearch task {taskUid} did not finish within {timeout.TotalSeconds:0} s."));
            }

            await Task.Delay(delay, time, cancellationToken);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxPoll.Ticks));
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var health = await SendAsync(HttpMethod.Get, "health", null, cancellationToken);
            return health?["status"]?.GetValue<string>() == "available";
        }
        catch (Exception ex) when (ex is SearchEngineUnavailableException or MeilisearchApiException)
        {
            return false;
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>Network failures, timeouts and an open circuit; never the caller's own cancellation.</summary>
    private static bool IsUnreachable(Exception ex, CancellationToken cancellationToken) => ex switch
    {
        HttpRequestException or TimeoutException or ExecutionRejectedException => true,
        OperationCanceledException => !cancellationToken.IsCancellationRequested,
        _ => false
    };
}

/// <summary>Meilisearch refused a request or a task failed: a bug or a configuration error (never retried).</summary>
internal sealed class MeilisearchApiException(HttpStatusCode? status, string code, string message)
    : InvalidOperationException($"Meilisearch {code}: {message}")
{
    public const string IndexNotFound = "index_not_found";
    public const string IndexAlreadyExists = "index_already_exists";

    public HttpStatusCode? Status { get; } = status;

    public string Code { get; } = code;

    public static MeilisearchApiException From(HttpStatusCode status, string? payload)
    {
        var error = TryParse(payload);
        return new MeilisearchApiException(status,
            error?["code"]?.GetValue<string>() ?? $"http_{(int)status}",
            error?["message"]?.GetValue<string>() ?? status.ToString());
    }

    public static MeilisearchApiException FromTask(JsonNode task)
    {
        var error = task["error"];
        return new MeilisearchApiException(null,
            error?["code"]?.GetValue<string>() ?? "task_failed",
            error?["message"]?.GetValue<string>() ?? $"task {task["uid"]} failed");
    }

    private static JsonNode? TryParse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;
        try
        {
            return JsonNode.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
