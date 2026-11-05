namespace Notification.Options;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Transport selection (Email:Provider): <c>Auto</c> (default), <c>Smtp</c> or <c>NoOp</c>.
    /// <c>Auto</c> = NoOp in Development when no real SMTP host is configured, otherwise Smtp.
    /// </summary>
    public EmailProvider Provider { get; set; } = EmailProvider.Auto;

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public bool UseTls { get; set; } = true;

    /// <summary>Product/brand name rendered in email layouts and subjects (Email:BrandName).</summary>
    public string BrandName { get; set; } = "StarterKit";

    /// <summary>Public base URL of the frontend app, used for links in emails (Email:AppBaseUrl).</summary>
    public string AppBaseUrl { get; set; } = "https://app.example.com";

    /// <summary>Support contact shown in emails (Email:SupportEmail).</summary>
    public string SupportEmail { get; set; } = "support@example.com";

    /// <summary>
    /// Extra hosts (besides the <see cref="AppBaseUrl"/> host) that links in emails may point to
    /// (Email:AllowedLinkHosts). Any other href rendered through the <c>safe_url</c> filter is replaced by "#".
    /// </summary>
    public string[] AllowedLinkHosts { get; set; } = [];

    /// <summary>Brand overrides of the email design system (Email:Theme). See EmailDesignTokens.</summary>
    public Notification.Channels.Email.EmailThemeOptions Theme { get; set; } = new();

    /// <summary>False when <see cref="SmtpHost"/> is blank or still the template's <c>example.com</c> placeholder.</summary>
    public bool IsSmtpHostConfigured =>
        !string.IsNullOrWhiteSpace(SmtpHost) &&
        !SmtpHost.EndsWith("example.com", StringComparison.OrdinalIgnoreCase);
}

public enum EmailProvider
{
    Auto = 0,
    Smtp = 1,
    NoOp = 2
}
