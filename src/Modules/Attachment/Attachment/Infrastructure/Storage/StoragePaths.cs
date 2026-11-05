using Attachment.Domains.Attachments.Exceptions;
using Microsoft.Net.Http.Headers;

namespace Attachment.Infrastructure.Storage;

/// <summary>Provider-neutral storage path rules and server-side path builders.</summary>
public static class StoragePaths
{
    private static readonly char[] InvalidSegmentChars = ['<', '>', ':', '"', '|', '?', '*', '\\'];

    /// <summary>
    /// Validates a '/'-separated relative storage key and returns its segments. Rejects empty paths,
    /// absolute paths, backslashes, '.'/'..' segments, empty segments and control/invalid characters.
    /// </summary>
    public static string[] ValidateAndSplit(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath) || storagePath.Length > 1000)
            throw new InvalidStoragePathException(storagePath ?? string.Empty);

        if (storagePath.StartsWith('/') || storagePath.Contains('\\') || storagePath.Contains(':'))
            throw new InvalidStoragePathException(storagePath);

        var segments = storagePath.Split('/');
        foreach (var segment in segments)
        {
            // Same rules on every OS (Windows-invalid characters are rejected on Linux too).
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') ||
                segment.Any(c => char.IsControl(c) || InvalidSegmentChars.Contains(c)))
            {
                throw new InvalidStoragePathException(storagePath);
            }
        }

        return segments;
    }

    public static string Validate(string? storagePath)
    {
        ValidateAndSplit(storagePath);
        return storagePath!;
    }

    /// <summary>
    /// Every object of a tenant lives under <c>t/{tenantId:N}/</c>, so per-tenant listing (orphan scan), purge and
    /// per-tenant buckets/containers are prefix operations. Objects written before multi-tenancy have no prefix and
    /// belong to the Default tenant.
    /// </summary>
    public const string TenantRootSegment = "t";

    public static string TenantRoot(Guid tenantId) => $"{TenantRootSegment}/{tenantId:N}/";

    /// <summary><c>t/{tenant}/yyyy/MM/dd/{id}{ext}</c> for a direct upload.</summary>
    public static string ForUpload(Guid tenantId, Guid id, string extension, DateTime utcNow) =>
        $"{TenantRoot(tenantId)}{utcNow:yyyy}/{utcNow:MM}/{utcNow:dd}/{id:N}{extension}";

    /// <summary><c>t/{tenant}/yyyy/MM/dd/thumb_{id}.jpg</c> for the thumbnail of a direct upload.</summary>
    public static string ForThumbnail(Guid tenantId, Guid id, DateTime utcNow) =>
        $"{TenantRoot(tenantId)}{utcNow:yyyy}/{utcNow:MM}/{utcNow:dd}/thumb_{id:N}.jpg";

    /// <summary>
    /// Thumbnail key next to the original: same folder, <c>thumb_{id}.jpg</c>. Works for direct uploads
    /// (<c>yyyy/MM/dd/</c>) and entity folders alike (and keeps the tenant prefix of the original).
    /// </summary>
    public static string ThumbnailBeside(string storagePath, Guid id)
    {
        var slash = storagePath.LastIndexOf('/');
        var folder = slash < 0 ? string.Empty : storagePath[..(slash + 1)];
        return folder + $"thumb_{id:N}.jpg";
    }

    public static string ForPending(Guid tenantId, Guid sessionId, Guid id, string extension) =>
        $"{TenantRoot(tenantId)}temp/{sessionId:N}/{id:N}{extension}";

    public static string ForPendingThumbnail(Guid tenantId, Guid sessionId, Guid id) =>
        $"{TenantRoot(tenantId)}temp/{sessionId:N}/thumb_{id:N}.jpg";

    public static string ForEntity(Guid tenantId, EntityType entityType, Guid entityId, Guid id, string extension) =>
        $"{TenantRoot(tenantId)}{entityType.ToString().ToLowerInvariant()}s/{entityId:N}/{id:N}{extension}";

    public static string ForEntityThumbnail(Guid tenantId, EntityType entityType, Guid entityId, Guid id) =>
        $"{TenantRoot(tenantId)}{entityType.ToString().ToLowerInvariant()}s/{entityId:N}/thumb_{id:N}.jpg";

    /// <summary>
    /// Whether <paramref name="storagePath"/> belongs to <paramref name="tenantId"/>: under its tenant root, or -
    /// for the Default tenant only - a legacy key without any tenant root.
    /// </summary>
    public static bool BelongsTo(string storagePath, Guid tenantId)
    {
        if (storagePath.StartsWith(TenantRoot(tenantId), StringComparison.Ordinal))
            return true;
        return tenantId == Shared.Core.MultiTenancy.TenantDefaults.DefaultTenantId &&
               !storagePath.StartsWith(TenantRootSegment + "/", StringComparison.Ordinal);
    }
}

/// <summary>Response header helpers shared by the download endpoints and the signed-URL providers.</summary>
public static class FileResponseHeaders
{
    /// <summary>
    /// <c>attachment</c> for everything except the configured safe raster image types. Never inline HTML,
    /// SVG, XML, PDF or text: they could run script or be rendered from the API origin.
    /// </summary>
    public static bool IsInline(string? contentType, IEnumerable<string> inlineContentTypes) =>
        contentType is not null &&
        inlineContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);

    /// <summary>RFC 6266 Content-Disposition value with an ASCII and a UTF-8 file name.</summary>
    public static string ContentDisposition(string? fileName, bool inline)
    {
        var header = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            header.SetHttpFileName(fileName);
        }

        return header.ToString();
    }
}
