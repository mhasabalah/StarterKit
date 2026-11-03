namespace Notification.Channels;

/// <summary>
/// Masks personal data before it reaches log sinks (Seq, console, Hangfire dashboard).
/// Keep enough to correlate a support ticket ("a***@example.com", "******4567"), never the full value.
/// </summary>
public static class PiiMask
{
    public static string Email(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "none";

        var at = email.IndexOf('@');
        if (at <= 0)
            return "***";

        return $"{email[0]}***{email[at..]}";
    }

    public static string Emails(IEnumerable<string>? emails)
        => emails is null ? "none" : string.Join(", ", emails.Select(Email));

    public static string Phone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return "none";

        var digits = phone.Trim();
        return digits.Length <= 4
            ? new string('*', digits.Length)
            : new string('*', digits.Length - 4) + digits[^4..];
    }
}
