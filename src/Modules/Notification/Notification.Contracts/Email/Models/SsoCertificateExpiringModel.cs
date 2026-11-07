namespace Notification.Contracts.Email.Models;

/// <summary>Sent to the tenant's SSO administrators when an SAML IdP signing certificate is about to expire.</summary>
public sealed class SsoCertificateExpiringModel
{
    public string ConnectionName { get; set; } = string.Empty;
    public string ExpiresOn { get; set; } = string.Empty;
    public int DaysLeft { get; set; }

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["ConnectionName"] = ConnectionName,
        ["ExpiresOn"] = ExpiresOn,
        ["DaysLeft"] = DaysLeft
    };
}
