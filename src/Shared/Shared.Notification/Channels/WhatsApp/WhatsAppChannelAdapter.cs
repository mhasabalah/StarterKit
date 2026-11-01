namespace Shared.Notification.Channels.WhatsApp;

internal sealed class WhatsAppChannelAdapter(
    WhatsAppApiClient apiClient,
    WhatsAppTemplateRegistry registry,
    IOptions<WhatsAppOptions> options,
    ILogger<WhatsAppChannelAdapter> logger) : INotificationChannelAdapter
{
    private readonly WhatsAppOptions _options = options.Value;

    public NotificationChannel Channel => NotificationChannel.WhatsApp;

    public bool CanSend(string? email, string? phone)
        => !string.IsNullOrWhiteSpace(phone);

    public async Task SendAsync(
        NotificationMessage message,
        string? email,
        string? phone,
        CancellationToken ct)
    {
        var payload = message.WhatsApp!;
        var templateConfig = registry.Resolve(payload.Template);

        var bodyParams = templateConfig.BodyParameters
            .Select(key => payload.Model?.TryGetValue(key, out var val) == true
                ? val?.ToString() ?? string.Empty
                : string.Empty)
            .ToArray();

        await apiClient.SendTemplateAsync(
            _options.PhoneNumberId,
            phone!,
            templateConfig.MetaTemplateName,
            templateConfig.LanguageCode,
            bodyParams,
            ct);

        logger.LogInformation(
            "WhatsApp notification sent. Template: {Template} Phone: {Phone}",
            payload.Template, phone);
    }
}
