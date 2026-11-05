using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Shared.Core.BotProtection;

/// <summary>
/// Cloudflare Turnstile adapter (<c>POST /turnstile/v0/siteverify</c>). The one place that knows the Turnstile wire
/// format; another provider is another class implementing <see cref="IHumanVerifier"/>.
/// </summary>
public sealed class TurnstileHumanVerifier(
    HttpClient httpClient,
    IOptions<BotProtectionOptions> options,
    BotProtectionMetrics metrics,
    ILogger<TurnstileHumanVerifier> logger) : IHumanVerifier
{
    public const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    /// <summary>Turnstile tokens are at most 2048 characters; anything longer is rejected without a network call.</summary>
    public const int MaxTokenLength = 2048;

    private readonly BotProtectionOptions _options = options.Value;

    public async Task<HumanVerificationResult> VerifyAsync(string? token, string? remoteIp, string action, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Reject(action, HumanVerificationResult.MissingTokenCode);
        if (token.Length > MaxTokenLength)
            return Reject(action, "invalid-input-response");

        var form = new Dictionary<string, string>
        {
            ["secret"] = _options.SecretKey,
            ["response"] = token,
            ["idempotency_key"] = Guid.NewGuid().ToString("D")
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
            form["remoteip"] = remoteIp;

        TurnstileResponse? body;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

            using var content = new FormUrlEncodedContent(form);
            using var response = await httpClient.PostAsync(new Uri(SiteVerifyUrl), content, timeout.Token);
            if ((int)response.StatusCode >= 500)
                return ProviderError(action, "provider 5xx", null);

            body = await response.Content.ReadFromJsonAsync<TurnstileResponse>(timeout.Token);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
                                   ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return ProviderError(action, ex.GetType().Name, ex);
        }

        if (body is null)
            return ProviderError(action, "empty response", null);

        if (!body.Success)
            return Reject(action, body.ErrorCodes is { Length: > 0 } ? body.ErrorCodes : ["verification-failed"]);

        if (_options.AllowedHostnames.Count > 0 &&
            !_options.AllowedHostnames.Contains(body.Hostname ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            return Reject(action, "hostname-mismatch");

        // The widget's data-action is echoed back; it is compared only when the widget set one.
        if (!string.IsNullOrEmpty(body.Action) && !string.Equals(body.Action, action, StringComparison.OrdinalIgnoreCase))
            return Reject(action, "action-mismatch");

        metrics.Record("success", action);
        return HumanVerificationResult.Passed;
    }

    private HumanVerificationResult Reject(string action, params string[] codes)
    {
        metrics.Record("failure", action);
        return HumanVerificationResult.Failed(codes);
    }

    private HumanVerificationResult ProviderError(string action, string reason, Exception? exception)
    {
        metrics.Record("provider_error", action);
        logger.LogWarning(exception,
            "Turnstile verification unavailable for action {Action} ({Reason}); {Decision}.",
            action, reason, _options.FailOpen ? "failing open" : "failing closed");

        return new HumanVerificationResult(_options.FailOpen, _options.FailOpen ? [] : ["provider-unavailable"], ProviderUnavailable: true);
    }

    private sealed record TurnstileResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes,
        [property: JsonPropertyName("hostname")] string? Hostname,
        [property: JsonPropertyName("action")] string? Action);
}
