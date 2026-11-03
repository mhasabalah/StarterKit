namespace Notification.Contracts.Email.Models;

/// <summary>Pure data bag; subject and template file live in the Notification module (the brand name is a template global).</summary>
public sealed class PasswordResetRequiredModel
{
    public string ResetUrl { get; set; } = string.Empty;
    public int ExpiresInHours { get; set; }

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["ResetUrl"] = ResetUrl,
        ["ExpiresInHours"] = ExpiresInHours
    };
}
