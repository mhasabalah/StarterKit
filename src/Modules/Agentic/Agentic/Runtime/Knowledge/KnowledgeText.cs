using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace Agentic.Runtime.Knowledge;

/// <summary>A document that cannot be indexed, whatever the number of attempts (unsupported, too large, no text).</summary>
public sealed class KnowledgeIndexingException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Turns stored content into plain text: the plain-text family (text, Markdown, CSV, JSON), HTML (markup removed) and
/// PDF with a text layer (PdfPig; scanned pages have no text and are reported as <c>no_text</c>). Bounded: the file is
/// read up to <c>MaxDocumentBytes</c>, a PDF up to <c>MaxPdfPages</c>, the text up to <c>MaxTextLength</c>.
/// </summary>
public static partial class KnowledgeTextExtractor
{
    private static readonly HashSet<string> PlainTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain", "text/markdown", "text/x-markdown", "text/csv", "application/json", "text/json"
    };

    private static readonly HashSet<string> PlainExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".json"
    };

    public static async Task<string> ExtractAsync(Stream content, string fileName, string contentType,
        KnowledgeOptions options, CancellationToken cancellationToken)
    {
        var type = contentType.Split(';')[0].Trim();
        var extension = Path.GetExtension(fileName);
        var bytes = await ReadLimitedAsync(content, options.MaxDocumentBytes, cancellationToken);

        string text;
        if (type.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            text = FromPdf(bytes, options.MaxPdfPages);
        }
        else if (type.Equals("text/html", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            text = FromHtml(Decode(bytes));
        }
        else if (PlainTypes.Contains(type) || PlainExtensions.Contains(extension))
        {
            text = Decode(bytes);
        }
        else
        {
            throw new KnowledgeIndexingException("unsupported_content_type");
        }

        return Normalize(text, options.MaxTextLength);
    }

    /// <summary>Line endings unified, trailing spaces and runs of blank lines removed; refuses empty and oversized text.</summary>
    public static string Normalize(string text, int maxLength)
    {
        var normalized = BlankLines().Replace(TrailingSpaces().Replace(text.Replace("\r\n", "\n").Replace('\r', '\n'), "\n"), "\n\n")
            .Replace("\0", string.Empty)
            .Trim();
        if (normalized.Length == 0)
            throw new KnowledgeIndexingException("no_text");
        if (normalized.Length > maxLength)
            throw new KnowledgeIndexingException("text_too_long");
        return normalized;
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new KnowledgeIndexingException("document_too_large");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string FromPdf(byte[] bytes, int maxPages)
    {
        try
        {
            using var document = PdfDocument.Open(bytes);
            if (document.NumberOfPages > maxPages)
                throw new KnowledgeIndexingException("document_too_large");

            var text = new StringBuilder();
            foreach (var page in document.GetPages())
                text.Append(page.Text).Append("\n\n");
            return text.ToString();
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new KnowledgeIndexingException("pdf_encrypted");
        }
        catch (Exception ex) when (ex is not KnowledgeIndexingException and not OperationCanceledException)
        {
            throw new KnowledgeIndexingException("pdf_unreadable");
        }
    }

    private static string FromHtml(string html)
    {
        var withoutCode = ScriptsAndStyles().Replace(html, " ");
        var withBreaks = BlockTags().Replace(withoutCode, "\n");
        return WebUtility.HtmlDecode(Tags().Replace(withBreaks, " "));
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    private static partial Regex ScriptsAndStyles();

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/h[1-6]|/tr)\b[^>]*>", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex BlockTags();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.None, 1000)]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}

/// <summary>One passage of a document and where it starts in the extracted text.</summary>
public readonly record struct TextChunk(int StartOffset, string Content);

/// <summary>
/// Splits text into overlapping passages of about <c>ChunkSize</c> characters, preferring to end a passage at a
/// paragraph, then a sentence, then a word boundary. Deterministic: the same text always gives the same passages.
/// </summary>
public static class KnowledgeChunker
{
    private static readonly string[] SentenceEnds = [". ", "! ", "? ", "؟ ", ".\n", "!\n", "?\n", "؟\n"];

    public static IReadOnlyList<TextChunk> Split(string text, int size, int overlap)
    {
        if (size < 1 || overlap < 0 || overlap >= size)
            throw new ArgumentOutOfRangeException(nameof(size), "0 <= overlap < size is required.");

        var chunks = new List<TextChunk>();
        var position = 0;
        while (position < text.Length)
        {
            var end = Math.Min(position + size, text.Length);
            if (end < text.Length)
                end = BreakPoint(text, position, end);

            var raw = text[position..end];
            var leading = raw.Length - raw.TrimStart().Length;
            var content = raw.Trim();
            if (content.Length > 0)
                chunks.Add(new TextChunk(position + leading, content));
            if (end >= text.Length)
                break;

            // Step back by the overlap, but start the next passage at a word boundary and always move forward.
            var next = Math.Max(end - overlap, position + 1);
            while (next < end && !char.IsWhiteSpace(text[next - 1]))
                next++;
            position = next;
        }

        return chunks;
    }

    private static int BreakPoint(string text, int start, int end)
    {
        var floor = start + ((end - start) / 2);
        var window = text[floor..end];

        var paragraph = window.LastIndexOf("\n\n", StringComparison.Ordinal);
        if (paragraph >= 0)
            return floor + paragraph + 2;

        var sentence = SentenceEnds.Select(s => window.LastIndexOf(s, StringComparison.Ordinal) is var i and >= 0 ? i + s.Length : -1).Max();
        if (sentence > 0)
            return floor + sentence;

        for (var i = end - 1; i > floor; i--)
        {
            if (char.IsWhiteSpace(text[i]))
                return i + 1;
        }

        return end;
    }
}
