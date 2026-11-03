using System.Net;

namespace Notification.Channels.WhatsApp;

internal sealed class WhatsAppApiClient(HttpClient httpClient)
{
    /// <summary>Provider error bodies are truncated to this length before they reach logs / the Error column.</summary>
    private const int MaxErrorBodyLength = 1000;

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

        using var response = await httpClient.PostAsJsonAsync($"{phoneNumberId}/messages", body, ct);
        if (response.IsSuccessStatusCode)
            return;

        string errorBody;
        try
        {
            errorBody = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errorBody = $"<unreadable body: {ex.GetType().Name}>";
        }

        if (errorBody.Length > MaxErrorBodyLength)
            errorBody = errorBody[..MaxErrorBodyLength];

        throw new WhatsAppApiException(response.StatusCode, errorBody);
    }
}

/// <summary>
/// Non-success response from the WhatsApp Cloud API. Carries the provider's error body (e.g. Meta's
/// <c>{"error":{"code":132001,"message":"Template name does not exist"}}</c>) so it lands in the
/// notification_log Error column instead of a bare "400 Bad Request".
/// </summary>
public sealed class WhatsAppApiException(HttpStatusCode statusCode, string responseBody)
    : Exception($"WhatsApp API returned {(int)statusCode} {statusCode}: {responseBody}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = responseBody;

    /// <summary>
    /// 4xx (except 408 Request Timeout and 429 Too Many Requests) means the request itself is wrong
    /// (bad template, invalid number, auth) - retrying cannot succeed.
    /// </summary>
    public bool IsPermanent
    {
        get
        {
            var code = (int)StatusCode;
            return code is >= 400 and < 500
                   && StatusCode != HttpStatusCode.RequestTimeout
                   && StatusCode != HttpStatusCode.TooManyRequests;
        }
    }
}
