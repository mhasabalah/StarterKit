namespace Shared.Core.BotProtection;

/// <summary>Which <see cref="IHumanVerifier"/> adapter to use. Adding a provider = one enum value + one registration line.</summary>
public enum BotProtectionProvider
{
    None = 0,
    Turnstile = 1
}

/// <summary>Configuration section <c>BotProtection</c>.</summary>
public sealed class BotProtectionOptions
{
    public const string SectionName = "BotProtection";

    /// <summary>Master switch. Off (default): every protected endpoint passes without a token.</summary>
    public bool Enabled { get; set; }

    public BotProtectionProvider Provider { get; set; } = BotProtectionProvider.Turnstile;

    /// <summary>Provider secret. Never commit it: use env <c>BotProtection__SecretKey</c> or the Docker secret file of the same name.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Public site key the frontend widget uses. Informational for the backend.</summary>
    public string SiteKey { get; set; } = string.Empty;

    /// <summary>When non-empty, the verified token's hostname must be one of these.</summary>
    public List<string> AllowedHostnames { get; set; } = [];

    /// <summary>Per-request timeout for the provider call.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// When the provider is unreachable or times out: true (default) lets the request through (logged, counted) so
    /// sign-in stays available; false rejects with 503 <c>captcha_unavailable</c>. An INVALID token is always rejected.
    /// </summary>
    public bool FailOpen { get; set; } = true;

    /// <summary>Actions that require verification. Empty = every endpoint that declares <c>.RequireHumanVerification</c>.</summary>
    public List<string> Actions { get; set; } = [];

    public bool Protects(string action) =>
        Actions.Count == 0 || Actions.Contains(action, StringComparer.OrdinalIgnoreCase);
}
