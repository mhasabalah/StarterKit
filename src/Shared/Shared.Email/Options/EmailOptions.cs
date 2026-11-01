namespace Shared.Email.Options;

public sealed class EmailOptions
{
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
}
