namespace Identity.Domains.Users.Models;

/// <summary>
/// The ONE display-name rule of the user directory (<c>UserDirectory.Contracts</c> summaries and profiles):
/// "First Last" when either is set, otherwise the email, otherwise the id - never empty.
/// </summary>
public static class UserDisplayName
{
    public static string Of(string? firstName, string? lastName, string? email, Guid userId)
    {
        var name = string.Join(' ', new[] { firstName, lastName }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim()));

        if (name.Length > 0) return name;
        return !string.IsNullOrWhiteSpace(email) ? email.Trim() : userId.ToString();
    }
}
