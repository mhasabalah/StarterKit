namespace Shared.Notification.Options;

public sealed class WhatsAppOptions
{
    public string ApiVersion { get; set; } = "v25.0";
    public string PhoneNumberId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public Dictionary<string, WhatsAppTemplateConfig> Templates { get; set; } = [];
}

public sealed class WhatsAppTemplateConfig
{
    public string MetaTemplateName { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = "en_US";
    public string[] BodyParameters { get; set; } = [];
}
