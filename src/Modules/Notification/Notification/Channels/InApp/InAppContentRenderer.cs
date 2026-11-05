using Notification.Contracts.InApp;

namespace Notification.Channels.InApp;

/// <summary>
/// Output safety for in-app entries: text is plain (clients must render it as text, never HTML), capped to the column
/// sizes; the action URL is either an app-relative path or an absolute link on an allow-listed host (the same list
/// the email <c>safe_url</c> filter uses). Anything else is dropped, so a notification can never carry a
/// <c>javascript:</c> or phishing link.
/// </summary>
public static class InAppContentRenderer
{
    public static string Text(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // Control characters (except line breaks in the body) have no business in UI text.
        var cleaned = new string(value.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray()).Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..(maxLength - 1)] + "…";
    }

    /// <summary>The safe action URL, or null when <paramref name="url"/> is absent or not allowed.</summary>
    public static string? ActionUrl(string? url, EmailOptions options)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var candidate = url.Trim();
        if (candidate.Length > InAppContent.ActionUrlMaxLength ||
            candidate.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\'))
            return null;

        // App-relative route ("/billing/invoices/42"). "//host" is protocol-relative = another origin: rejected.
        if (candidate.StartsWith('/'))
            return candidate.StartsWith("//", StringComparison.Ordinal) ? null : candidate;

        var safe = EmailContentSafety.SanitizeUrl(candidate, options);
        return safe == EmailContentSafety.RejectedUrl ? null : safe;
    }

    /// <summary>Copies at most <paramref name="maxEntries"/> non-empty keys, each key/value capped.</summary>
    public static Dictionary<string, string>? Data(IReadOnlyDictionary<string, string>? data, int maxEntries)
    {
        if (data is not { Count: > 0 })
            return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in data)
        {
            if (result.Count >= maxEntries)
                break;
            var cleanKey = Text(key, 100);
            if (cleanKey.Length > 0)
                result.TryAdd(cleanKey, Text(value, 1000));
        }

        return result.Count > 0 ? result : null;
    }
}
