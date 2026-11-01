namespace Shared.Notification.Channels.WhatsApp;

internal sealed class WhatsAppApiClient(HttpClient httpClient)
{
    public async Task SendTemplateAsync(
        string phoneNumberId,
        string recipientPhone,
        string metaTemplateName,
        string languageCode,
        string[] bodyParameterValues,
        CancellationToken ct)
    {
        var body = new
        {
            messaging_product = "whatsapp",
            to = recipientPhone,
            type = "template",
            template = new
            {
                name = metaTemplateName,
                language = new { code = languageCode },
                components = bodyParameterValues.Length == 0
                    ? Array.Empty<object>()
                    : new object[]
                    {
                        new
                        {
                            type = "body",
                            parameters = bodyParameterValues
                                .Select(v => new { type = "text", text = v })
                                .ToArray()
                        }
                    }
            }
        };

        var response = await httpClient.PostAsJsonAsync($"{phoneNumberId}/messages", body, ct);
        response.EnsureSuccessStatusCode();
    }
}
