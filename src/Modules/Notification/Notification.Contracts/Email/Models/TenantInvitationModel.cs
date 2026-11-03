namespace Notification.Contracts.Email.Models;

/// <summary>Invitation to join a tenant (multi-tenancy). Pure data bag; identity/subjects in <see cref="Metadata"/>.</summary>
public sealed class TenantInvitationModel
{
    public string InviterName { get; set; } = string.Empty;
    public string TenantName { get; set; } = string.Empty;
    public string AcceptUrl { get; set; } = string.Empty;
    public int ExpiresInDays { get; set; }

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["InviterName"] = InviterName,
        ["TenantName"] = TenantName,
        ["AcceptUrl"] = AcceptUrl,
        ["ExpiresInDays"] = ExpiresInDays
    };
}
