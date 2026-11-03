namespace Notification.Contracts.Email.Models;

/// <summary>Pure data bag; subject and template file live in the Notification module.</summary>
public sealed class WelcomeUserModel
{
    public string FirstName { get; set; } = string.Empty;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["firstName"] = FirstName
    };
}
