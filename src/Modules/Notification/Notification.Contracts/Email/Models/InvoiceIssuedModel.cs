namespace Notification.Contracts.Email.Models;

public sealed class InvoiceIssuedModel
{
    public string SubscriberName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string InvoiceType { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string IssuedAt { get; set; } = string.Empty;
    public string PortalUrl { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["SubscriberName"] = SubscriberName,
        ["InvoiceNumber"] = InvoiceNumber,
        ["InvoiceType"] = InvoiceType,
        ["Amount"] = Amount,
        ["Currency"] = Currency,
        ["IssuedAt"] = IssuedAt,
        ["PortalUrl"] = PortalUrl
    };
}
