using System.IO.Compression;
using System.Text;

namespace Attachment.Infrastructure.Validation;

/// <summary>
/// Content (magic-byte) rules for every extension the module can accept. The rule chosen by the extension
/// decides the SERVER-side content type, attachment type and stored extension; the file content must match
/// it. An allowed extension without a rule here is rejected (deny by default).
/// </summary>
public static class FileSignatureCatalog
{
    /// <summary>Always blocked, even if listed in <c>AllowedExtensions</c>: active content or executables.</summary>
    public static readonly IReadOnlySet<string> AlwaysBlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xhtml", ".shtml", ".mht", ".mhtml", ".svg", ".svgz", ".xsl", ".xslt",
        ".js", ".mjs", ".cjs", ".jsx", ".ts", ".vbs", ".vbe", ".wsf", ".wsh", ".hta", ".swf",
        ".exe", ".dll", ".com", ".scr", ".msi", ".msp", ".bat", ".cmd", ".ps1", ".psm1", ".sh", ".bash",
        ".jar", ".class", ".apk", ".app", ".cpl", ".lnk", ".reg", ".php", ".phtml", ".asp", ".aspx",
        ".cshtml", ".jsp", ".cgi", ".pl", ".py", ".rb", ".docm", ".xlsm", ".pptm", ".dotm", ".xltm"
    };

    private static readonly Dictionary<string, FileTypeRule> Rules = BuildRules();

    public static FileTypeRule? Find(string extension) =>
        Rules.TryGetValue(extension.ToLowerInvariant(), out var rule) ? rule : null;

    public static IEnumerable<string> SupportedExtensions => Rules.Keys;

    private static Dictionary<string, FileTypeRule> BuildRules()
    {
        var rules = new List<FileTypeRule>
        {
            // Images
            Binary(".jpg", "image/jpeg", AttachmentType.Image, h => StartsWith(h, 0xFF, 0xD8, 0xFF), "image/jpg", "image/pjpeg"),
            Binary(".jpeg", "image/jpeg", AttachmentType.Image, h => StartsWith(h, 0xFF, 0xD8, 0xFF), "image/jpg", "image/pjpeg"),
            Binary(".png", "image/png", AttachmentType.Image, h => StartsWith(h, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A), "image/x-png"),
            Binary(".gif", "image/gif", AttachmentType.Image, h => StartsWithAscii(h, "GIF87a") || StartsWithAscii(h, "GIF89a")),
            Binary(".bmp", "image/bmp", AttachmentType.Image, h => StartsWithAscii(h, "BM") && h.Length >= 14, "image/x-bmp", "image/x-ms-bmp"),
            Binary(".webp", "image/webp", AttachmentType.Image, h => StartsWithAscii(h, "RIFF") && AsciiAt(h, 8, "WEBP")),

            // Documents
            Binary(".pdf", "application/pdf", AttachmentType.Document, h => StartsWithAscii(h, "%PDF-"), "application/x-pdf"),
            Binary(".doc", "application/msword", AttachmentType.Document, IsOle),
            Binary(".xls", "application/vnd.ms-excel", AttachmentType.Document, IsOle),
            Binary(".ppt", "application/vnd.ms-powerpoint", AttachmentType.Document, IsOle),
            Zip(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "word/"),
            Zip(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xl/"),
            Zip(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", "ppt/"),

            // Text (no magic bytes: validated as UTF-8 text without markup)
            Text(".txt", "text/plain", TextKind.Plain),
            Text(".csv", "text/csv", TextKind.Plain, "application/csv", "text/plain", "application/vnd.ms-excel"),
            Text(".json", "application/json", TextKind.Json, "text/json", "text/plain"),
            Text(".xml", "application/xml", TextKind.Xml, "text/xml"),

            // Video
            Binary(".mp4", "video/mp4", AttachmentType.Video, h => AsciiAt(h, 4, "ftyp")),
            Binary(".mov", "video/quicktime", AttachmentType.Video,
                h => AsciiAt(h, 4, "ftyp") || AsciiAt(h, 4, "moov") || AsciiAt(h, 4, "mdat") || AsciiAt(h, 4, "wide")),
            Binary(".mpeg", "video/mpeg", AttachmentType.Video, IsMpegVideo),
            Binary(".mpg", "video/mpeg", AttachmentType.Video, IsMpegVideo),
            Binary(".avi", "video/x-msvideo", AttachmentType.Video, h => StartsWithAscii(h, "RIFF") && AsciiAt(h, 8, "AVI "), "video/avi", "video/msvideo"),

            // Audio
            Binary(".mp3", "audio/mpeg", AttachmentType.Audio,
                h => StartsWithAscii(h, "ID3") || (h.Length >= 2 && h[0] == 0xFF && (h[1] & 0xE0) == 0xE0), "audio/mp3", "audio/mpeg3"),
            Binary(".wav", "audio/wav", AttachmentType.Audio, h => StartsWithAscii(h, "RIFF") && AsciiAt(h, 8, "WAVE"), "audio/x-wav", "audio/wave"),
            Binary(".ogg", "audio/ogg", AttachmentType.Audio, h => StartsWithAscii(h, "OggS"), "application/ogg"),
        };

        return rules.ToDictionary(r => r.Extension, StringComparer.OrdinalIgnoreCase);
    }

    private static FileTypeRule Binary(string ext, string contentType, AttachmentType type,
        Func<byte[], bool> matcher, params string[] aliases) =>
        new(ext, contentType, type, aliases, matcher, null, null);

    private static FileTypeRule Zip(string ext, string contentType, string requiredEntryPrefix) =>
        new(ext, contentType, AttachmentType.Document, [], h => StartsWith(h, 0x50, 0x4B, 0x03, 0x04), requiredEntryPrefix, null);

    private static FileTypeRule Text(string ext, string contentType, TextKind kind, params string[] aliases) =>
        new(ext, contentType, AttachmentType.Document, aliases, _ => true, null, kind);

    private static bool IsOle(byte[] h) => StartsWith(h, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1);

    private static bool IsMpegVideo(byte[] h) =>
        StartsWith(h, 0x00, 0x00, 0x01, 0xBA) || StartsWith(h, 0x00, 0x00, 0x01, 0xB3);

    private static bool StartsWith(byte[] header, params byte[] signature) =>
        header.Length >= signature.Length && header.AsSpan(0, signature.Length).SequenceEqual(signature);

    private static bool StartsWithAscii(byte[] header, string signature) => AsciiAt(header, 0, signature);

    private static bool AsciiAt(byte[] header, int offset, string text) =>
        header.Length >= offset + text.Length &&
        header.AsSpan(offset, text.Length).SequenceEqual(Encoding.ASCII.GetBytes(text));
}

public enum TextKind
{
    Plain,
    Json,
    Xml
}

/// <param name="Extension">Lower-case extension including the dot (also the stored extension).</param>
/// <param name="ContentType">Server-side content type stored and served for this file.</param>
/// <param name="DeclaredAliases">Other client-declared content types accepted for this rule.</param>
/// <param name="HeaderMatches">Magic-byte check on the first bytes of the file.</param>
/// <param name="ZipEntryPrefix">OOXML: the zip must contain [Content_Types].xml and an entry with this prefix.</param>
/// <param name="TextKind">Text formats: validated as UTF-8 text without HTML/script markup.</param>
public sealed record FileTypeRule(
    string Extension,
    string ContentType,
    AttachmentType Type,
    string[] DeclaredAliases,
    Func<byte[], bool> HeaderMatches,
    string? ZipEntryPrefix,
    TextKind? TextKind)
{
    public bool AcceptsDeclared(string declared) =>
        string.Equals(declared, ContentType, StringComparison.OrdinalIgnoreCase) ||
        DeclaredAliases.Contains(declared, StringComparer.OrdinalIgnoreCase);

    internal static bool IsValidOfficeZip(Stream stream, string requiredEntryPrefix)
    {
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var hasContentTypes = false;
            var hasPart = false;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName == "[Content_Types].xml") hasContentTypes = true;
                if (entry.FullName.StartsWith(requiredEntryPrefix, StringComparison.Ordinal)) hasPart = true;
                if (entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)) return false;
            }

            return hasContentTypes && hasPart;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
