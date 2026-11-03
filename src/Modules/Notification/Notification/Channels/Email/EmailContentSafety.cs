using System.Text;

namespace Notification.Channels.Email;

/// <summary>
/// Output-safety rules for outgoing email: link allow-listing (used by the Liquid <c>safe_url</c> filter)
/// and header sanitising for subjects (no CR/LF header injection).
/// </summary>
public static class EmailContentSafety
{
    /// <summary>Value substituted for a rejected link. Harmless in an href and obviously broken in QA.</summary>
    public const string RejectedUrl = "#";

    private const int MaxSubjectLength = 255;

    /// <summary>
    /// Returns <paramref name="url"/> when it is an absolute http/https URL whose host is the
    /// <see cref="EmailOptions.AppBaseUrl"/> host or one of <see cref="EmailOptions.AllowedLinkHosts"/>;
    /// otherwise <see cref="RejectedUrl"/>. Rejects javascript:/data:/mailto:, relative and
    /// protocol-relative URLs, userinfo tricks (https://app.example.com@evil.test) and look-alike hosts.
    /// </summary>
    public static string SanitizeUrl(string? url, EmailOptions options)
    {
        if (string.IsNullOrWhiteSpace(url))
            return RejectedUrl;

        var candidate = url.Trim();

        // Control characters / whitespace inside a URL are a classic filter-evasion trick.
        if (candidate.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            return RejectedUrl;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return RejectedUrl;

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return RejectedUrl;

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return RejectedUrl;

        return IsAllowedHost(uri.Host, options) ? uri.AbsoluteUri : RejectedUrl;
    }

    public static bool IsAllowedHost(string host, EmailOptions options)
    {
        if (Uri.TryCreate(options.AppBaseUrl, UriKind.Absolute, out var appBase) &&
            string.Equals(appBase.Host, host, StringComparison.OrdinalIgnoreCase))
            return true;

        return options.AllowedLinkHosts.Any(h =>
            string.Equals(h.Trim(), host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Makes a subject safe for the Subject header: strips CR/LF and every other control character,
    /// collapses whitespace and caps the length. MimeKit performs RFC 2047 encoding afterwards.
    /// </summary>
    public static string SanitizeSubject(string? subject)
    {
        if (string.IsNullOrEmpty(subject))
            return string.Empty;

        var sb = new StringBuilder(subject.Length);
        var lastWasSpace = false;
        foreach (var c in subject)
        {
            // IsWhiteSpace also covers U+2028/U+2029 (line/paragraph separators).
            var isSpace = char.IsControl(c) || char.IsWhiteSpace(c);
            if (isSpace)
            {
                if (!lastWasSpace && sb.Length > 0)
                    sb.Append(' ');
                lastWasSpace = true;
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        var result = sb.ToString().Trim();
        return result.Length > MaxSubjectLength ? result[..MaxSubjectLength] : result;
    }
}
