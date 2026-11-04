using System.Text;
using Attachment.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Attachment.Infrastructure.ImageProcessing;

namespace Attachment.Infrastructure.Validation;

/// <summary>
/// Upload validation: extension allow/block lists, magic-byte content detection (server decides the type),
/// declared-type consistency, per-type size limits, image megapixel cap (checked with
/// <see cref="IImageProcessingService.IdentifyAsync"/>, i.e. before any decoding) and optional malware scanning.
/// </summary>
public sealed class FileValidationService(
    IOptions<AttachmentOptions> options,
    IMalwareScanner malwareScanner,
    IImageProcessingService images,
    ILogger<FileValidationService> logger) : IFileValidationService
{
    private const int HeaderLength = 8 * 1024;
    private const int TextSniffLength = 64 * 1024;

    private static readonly string[] MarkupMarkers =
    [
        "<script", "<html", "<!doctype html", "<svg", "<iframe", "<object", "<embed", "javascript:",
        "<body", "<head", "<meta", "<link", "<?php", "<%", "onload=", "onerror="
    ];

    public async Task<FileValidationResult> ValidateFileAsync(
        Stream fileStream,
        string fileName,
        string declaredContentType,
        long declaredFileSize,
        AttachmentType requestedType,
        CancellationToken cancellationToken = default)
    {
        var safeName = SanitizeFileName(fileName);
        var result = await ValidateCoreAsync(
            fileStream, safeName, declaredContentType, declaredFileSize, requestedType, cancellationToken);

        if (fileStream.CanSeek)
            fileStream.Position = 0;

        if (!result.IsValid)
        {
            logger.LogWarning(
                "File validation failed for {FileName} (declared {ContentType}, {FileSize} bytes): {Errors}",
                safeName, declaredContentType, declaredFileSize, string.Join("; ", result.Errors));
        }

        return result;
    }

    private async Task<FileValidationResult> ValidateCoreAsync(
        Stream stream,
        string safeName,
        string declaredContentType,
        long declaredFileSize,
        AttachmentType requestedType,
        CancellationToken cancellationToken)
    {
        var v = options.Value.Validation;

        if (!stream.CanSeek || !stream.CanRead)
            return FileValidationResult.Invalid(safeName, "The upload stream must be readable and seekable.");

        stream.Position = 0;
        var size = stream.Length;
        if (size <= 0)
            return FileValidationResult.Invalid(safeName, "The file is empty.");

        // 1. Extension: block list wins over allow list.
        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
            return FileValidationResult.Invalid(safeName, "The file has no extension.");

        if (FileSignatureCatalog.AlwaysBlockedExtensions.Contains(extension) ||
            v.BlockedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return FileValidationResult.Invalid(safeName, $"File extension '{extension}' is not allowed for security reasons.");

        if (!v.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return FileValidationResult.Invalid(safeName, $"File extension '{extension}' is not allowed.");

        var rule = FileSignatureCatalog.Find(extension);
        if (rule is null)
            return FileValidationResult.Invalid(safeName, $"File extension '{extension}' has no content verification rule.");

        var errors = new List<string>();

        // 2. Requested type vs real type (Other = let the server decide).
        if (requestedType != AttachmentType.Other && requestedType != rule.Type)
            errors.Add($"A '{extension}' file is a {rule.Type}, not a {requestedType}.");

        // 3. Declared content type must agree (octet-stream / empty = not declared).
        var declared = NormalizeContentType(declaredContentType);
        if (declared is not ("" or "application/octet-stream") && !rule.AcceptsDeclared(declared))
            errors.Add($"Declared content type '{declared}' does not match a '{extension}' file ({rule.ContentType}).");

        // 4. Server-determined content type must be allowed for its type.
        if (!AllowedTypesFor(rule.Type, v).Contains(rule.ContentType, StringComparer.OrdinalIgnoreCase))
            errors.Add($"File type '{rule.ContentType}' is not allowed.");

        // 5. Size: the real length, never only the client value.
        var maxSize = MaxSizeFor(rule.Type, v);
        if (size > maxSize || declaredFileSize > maxSize)
            errors.Add($"File size exceeds the maximum allowed size of {FormatBytes(maxSize)}.");

        if (errors.Count > 0)
            return new FileValidationResult(false, errors, safeName, null);

        // 6. Content must match the extension (magic bytes / text / zip structure).
        var contentError = await VerifyContentAsync(stream, rule, cancellationToken);
        if (contentError is not null)
            return FileValidationResult.Invalid(safeName, contentError);

        int? width = null, height = null;

        // 7. Images: identify (header only, no pixel decode) and cap the megapixels.
        if (rule.Type == AttachmentType.Image)
        {
            stream.Position = 0;
            var info = await images.IdentifyAsync(stream, cancellationToken);

            if (info is null)
                return FileValidationResult.Invalid(safeName, "File is not a valid image.");

            if (!string.Equals(info.MimeType, rule.ContentType, StringComparison.OrdinalIgnoreCase))
                return FileValidationResult.Invalid(safeName, $"Image content is not a {rule.ContentType} image.");

            var megapixels = (double)info.Width * info.Height / 1_000_000d;
            if (info.Width <= 0 || info.Height <= 0 || megapixels > v.MaxImageMegapixels)
                return FileValidationResult.Invalid(safeName,
                    $"Image dimensions {info.Width}x{info.Height} exceed the maximum of {v.MaxImageMegapixels} megapixels.");

            width = info.Width;
            height = info.Height;
        }

        // 8. Malware scan (startup guarantees a real scanner when enabled).
        if (v.EnableVirusScanning)
        {
            stream.Position = 0;
            var scan = await malwareScanner.ScanAsync(stream, safeName, cancellationToken);
            if (!scan.IsClean)
            {
                logger.LogWarning("Malware detected in upload {FileName}: {Threat}", safeName, scan.ThreatName);
                return FileValidationResult.Invalid(safeName, "The file was rejected by the malware scanner.");
            }
        }

        return new FileValidationResult(true, [], safeName,
            new DetectedFileType(rule.Extension, rule.ContentType, rule.Type, size, width, height));
    }

    private static async Task<string?> VerifyContentAsync(Stream stream, FileTypeRule rule, CancellationToken ct)
    {
        stream.Position = 0;

        if (rule.TextKind is { } kind)
        {
            var sniff = await ReadAsync(stream, TextSniffLength, ct);
            return VerifyText(sniff, stream.Length > sniff.Length, kind, rule.Extension);
        }

        var header = await ReadAsync(stream, HeaderLength, ct);
        if (!rule.HeaderMatches(header))
            return $"File content does not match the '{rule.Extension}' extension.";

        if (rule.ZipEntryPrefix is { } prefix)
        {
            stream.Position = 0;
            if (!FileTypeRule.IsValidOfficeZip(stream, prefix))
                return $"File content is not a valid '{rule.Extension}' document (or contains macros).";
        }

        return null;
    }

    private static string? VerifyText(byte[] bytes, bool truncated, TextKind kind, string extension)
    {
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            span = span[3..];

        // Drop an incomplete trailing UTF-8 sequence when we only read the start of a larger file.
        if (truncated)
            span = TrimIncompleteUtf8(span);

        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(span);
        }
        catch (DecoderFallbackException)
        {
            return $"File content is not valid UTF-8 text for a '{extension}' file.";
        }

        if (text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n' or '\f')))
            return $"File content is binary, not a '{extension}' text file.";

        var lower = text.ToLowerInvariant();
        if (MarkupMarkers.Any(lower.Contains))
            return "Text files must not contain HTML or script markup.";

        var first = text.TrimStart();
        return kind switch
        {
            TextKind.Json when first.Length == 0 || first[0] is not ('{' or '[') =>
                "File content is not JSON.",
            TextKind.Xml when !first.StartsWith('<') =>
                "File content is not XML.",
            TextKind.Xml when lower.Contains("<!doctype") || lower.Contains("<!entity") =>
                "XML files with DOCTYPE/ENTITY declarations are not allowed.",
            TextKind.Plain when first.StartsWith('<') =>
                $"File content looks like markup, not a '{extension}' file.",
            _ => null
        };
    }

    private static ReadOnlySpan<byte> TrimIncompleteUtf8(ReadOnlySpan<byte> span)
    {
        // Walk back over continuation bytes (10xxxxxx) to the lead byte of the last sequence.
        var i = span.Length - 1;
        var continuation = 0;
        while (i >= 0 && continuation < 3 && (span[i] & 0xC0) == 0x80)
        {
            i--;
            continuation++;
        }

        if (i < 0)
            return span;

        var lead = span[i];
        var expected = lead switch
        {
            < 0x80 => 1,
            >= 0xF0 => 4,
            >= 0xE0 => 3,
            >= 0xC0 => 2,
            _ => 1
        };

        return continuation + 1 < expected ? span[..i] : span;
    }

    private static async Task<byte[]> ReadAsync(Stream stream, int max, CancellationToken ct)
    {
        var buffer = new byte[(int)Math.Min(max, stream.Length)];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0)
                break;
            read += n;
        }

        return read == buffer.Length ? buffer : buffer[..read];
    }

    /// <summary>File name without any directory part or control characters; "file" when nothing is left.</summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?')).ToArray())
            .Trim().TrimEnd('.');

        if (name.Length > 255)
        {
            var ext = Path.GetExtension(name);
            name = name[..(255 - ext.Length)] + ext;
        }

        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }

    private static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return string.Empty;
        var semicolon = contentType.IndexOf(';');
        return (semicolon >= 0 ? contentType[..semicolon] : contentType).Trim().ToLowerInvariant();
    }

    private static string[] AllowedTypesFor(AttachmentType type, ValidationOptions v) => type switch
    {
        AttachmentType.Image => v.AllowedImageTypes,
        AttachmentType.Document => v.AllowedDocumentTypes,
        AttachmentType.Video => v.AllowedVideoTypes,
        AttachmentType.Audio => v.AllowedAudioTypes,
        _ => []
    };

    private static long MaxSizeFor(AttachmentType type, ValidationOptions v) => type switch
    {
        AttachmentType.Image => v.MaxImageSizeBytes,
        AttachmentType.Document => v.MaxDocumentSizeBytes,
        AttachmentType.Video => v.MaxVideoSizeBytes,
        AttachmentType.Audio => v.MaxAudioSizeBytes,
        _ => v.MaxFileSizeBytes
    };

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}
