namespace Notification.Contracts.Email.Models;

/// <summary>Pure data bag; subject and template file live in the Notification module (the brand name is a template global).</summary>
public sealed class EmailConfirmationModel
{
    public string FirstName { get; set; } = string.Empty;
    public string ConfirmUrl { get; set; } = string.Empty;
    public int ExpiresInHours { get; set; }

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["FirstName"] = FirstName,
        ["ConfirmUrl"] = ConfirmUrl,
        ["ExpiresInHours"] = ExpiresInHours
    };
}
