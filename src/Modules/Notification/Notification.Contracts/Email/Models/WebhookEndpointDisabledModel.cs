namespace Notification.Contracts.Email.Models;

/// <summary>Sent to the tenant owner when a webhook endpoint was disabled after repeated delivery failures.</summary>
public sealed class WebhookEndpointDisabledModel
{
    public string TenantName { get; set; } = string.Empty;
    public string EndpointAddress { get; set; } = string.Empty;
    public int ConsecutiveFailures { get; set; }
    public string LastError { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["TenantName"] = TenantName,
        ["EndpointAddress"] = EndpointAddress,
        ["ConsecutiveFailures"] = ConsecutiveFailures,
        ["LastError"] = LastError
    };
}
