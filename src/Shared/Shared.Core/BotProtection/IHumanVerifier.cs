namespace Shared.Core.BotProtection;

/// <summary>
/// Port for "is this request from a human?" challenges (CAPTCHA). Providers (Cloudflare Turnstile, hCaptcha,
/// reCAPTCHA, ...) are adapters behind this interface; endpoints never reference a provider. Endpoints opt in with
/// <c>.RequireHumanVerification("action")</c>.
/// </summary>
public interface IHumanVerifier
{
    /// <param name="token">The challenge response token produced by the client widget (null/blank when absent).</param>
    /// <param name="remoteIp">The client IP, forwarded to the provider as an additional signal (optional).</param>
    /// <param name="action">The protected action name (e.g. <c>login</c>); also checked against the token when the provider supports it.</param>
    /// <param name="cancellationToken">Cancels the verification.</param>
    Task<HumanVerificationResult> VerifyAsync(string? token, string? remoteIp, string action, CancellationToken cancellationToken);
}

/// <summary>Outcome of one verification.</summary>
/// <param name="Success">True when the request may proceed (a verified human, or fail-open after a provider outage).</param>
/// <param name="ErrorCodes">Provider/adapter error codes when <paramref name="Success"/> is false (diagnostic, never shown to users).</param>
/// <param name="ProviderUnavailable">The provider could not be reached (timeout, network error, 5xx). With fail-open <paramref name="Success"/> is true.</param>
public sealed record HumanVerificationResult(
    bool Success,
    IReadOnlyList<string> ErrorCodes,
    bool ProviderUnavailable = false)
{
    /// <summary>Error code meaning the client sent no token at all.</summary>
    public const string MissingTokenCode = "missing-input-response";

    public static HumanVerificationResult Passed { get; } = new(true, []);

    public static HumanVerificationResult Failed(params string[] errorCodes) => new(false, errorCodes);

    public bool IsMissingToken => !Success && ErrorCodes.Contains(MissingTokenCode, StringComparer.Ordinal);
}
