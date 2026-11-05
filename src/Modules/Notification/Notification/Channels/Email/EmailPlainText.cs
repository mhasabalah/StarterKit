using System.Net;
using System.Text.RegularExpressions;

namespace Notification.Channels.Email;

/// <summary>
/// Derives the text/plain alternative from the rendered HTML, so every email is multipart/alternative without a
/// second template per message. Keeps what matters in plain text: headings and paragraphs as lines, links as
/// "label (url)", the one-time code readable; drops styles, comments, MSO blocks and the hidden preheader.
/// </summary>
public static partial class EmailPlainText
{
    public static string FromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = Head().Replace(html, string.Empty);
        text = Comments().Replace(text, string.Empty);
        text = Hidden().Replace(text, string.Empty);
        text = Links().Replace(text, m =>
        {
            var label = Tags().Replace(m.Groups["label"].Value, string.Empty).Trim();
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value).Trim();
            if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || href == "#" || href.Length == 0)
                return label;
            return label.Length == 0 || label == href ? href : $"{label} ({href})";
        });
        text = BlockEnds().Replace(text, "\n");
        text = LineBreaks().Replace(text, "\n");
        text = Tags().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text).Replace(' ', ' ').Replace("‌", string.Empty, StringComparison.Ordinal);

        var lines = text.Split('\n')
            .Select(l => Spaces().Replace(l, " ").Trim())
            .ToList();

        // Collapse runs of empty lines to one.
        var result = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Length == 0 && (result.Count == 0 || result[^1].Length == 0))
                continue;
            result.Add(line);
        }

        return string.Join("\n", result).Trim();
    }

    [GeneratedRegex("<head[^>]*>.*?</head>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Head();

    [GeneratedRegex("<!--.*?-->", RegexOptions.Singleline, 1000)]
    private static partial Regex Comments();

    [GeneratedRegex("<div[^>]*display:\\s*none[^>]*>.*?</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Hidden();

    [GeneratedRegex("<a\\s[^>]*href=\"(?<href>[^\"]*)\"[^>]*>(?<label>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Links();

    [GeneratedRegex("</(p|h[1-6]|tr|div|li|table)>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex BlockEnds();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex LineBreaks();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline, 1000)]
    private static partial Regex Tags();

    [GeneratedRegex("[ \\t\\r]+", RegexOptions.None, 1000)]
    private static partial Regex Spaces();
}
