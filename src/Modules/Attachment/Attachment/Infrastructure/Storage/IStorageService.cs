namespace Attachment.Infrastructure.Storage;

/// <summary>
/// Blob storage abstraction (Local / Azure Blob / S3).
/// <para>
/// Storage paths are provider-neutral, '/'-separated, relative keys (e.g. <c>2026/11/01/{id}.png</c>).
/// They are the source of truth: URLs are never stored, they are created per request with
/// <see cref="GetReadUrlAsync"/>.
/// </para>
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// True when the application streams the bytes itself (Local). False when clients are redirected to a
    /// signed provider URL (S3 / Azure).
    /// </summary>
    bool ServesThroughApplication { get; }

    /// <summary>
    /// Writes <paramref name="fileStream"/> (from its current position) to <paramref name="storagePath"/>.
    /// The returned size is the number of bytes written; providers take it from the (seekable) stream instead of
    /// re-reading the object's metadata.
    /// </summary>
    Task<StorageResult> UploadToPathAsync(
        Stream fileStream,
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the file for reading and returns the PROVIDER stream (file stream / network response stream), never a
    /// buffered copy, so callers can stream large files. The caller disposes it.
    /// Throws <c>StoredFileNotFoundException</c> when it does not exist.
    /// </summary>
    Task<Stream> OpenReadAsync(
        string storagePath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storagePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes several files. Never throws for a single failed file: the paths that could not be deleted
    /// are returned, so callers keep the matching DB rows and retry later. Missing files count as deleted.
    /// </summary>
    Task<BatchDeleteResult> DeleteBatchAsync(
        IEnumerable<string> storagePaths,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string storagePath,
        CancellationToken cancellationToken = default);

    Task<StorageResult> CopyAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default);

    Task<StorageResult> MoveAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A short-lived URL that lets a browser read the file without other credentials:
    /// S3 presigned GET, Azure SAS, or a signed application route for Local storage.
    /// </summary>
    Task<string> GetReadUrlAsync(
        string storagePath,
        TimeSpan ttl,
        ReadUrlOptions? options = null,
        CancellationToken cancellationToken = default);
}

public record StorageResult(string StoragePath, long FileSize);

/// <summary>
/// Optional provider capability: enumerate every stored object. Used by the reconciliation sweeper to find blobs
/// that no row references. All built-in providers (Local, S3, Azure) implement it.
/// </summary>
public interface IStorageListing
{
    /// <param name="prefix">Only keys starting with this prefix (e.g. a tenant root <c>t/{tenantId}/</c>); null = all.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    IAsyncEnumerable<StoredObjectInfo> ListAsync(string? prefix = null, CancellationToken cancellationToken = default);
}

/// <param name="StoragePath">Provider-neutral '/'-separated key.</param>
/// <param name="LastModifiedUtc">Last write (or copy/move) time.</param>
public sealed record StoredObjectInfo(string StoragePath, DateTimeOffset LastModifiedUtc);

public static class StorageStreams
{
    /// <summary>Bytes left in a seekable stream, or null when the length is unknown.</summary>
    public static long? RemainingLength(Stream stream)
    {
        try
        {
            return stream.CanSeek ? Math.Max(0, stream.Length - stream.Position) : null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>Response headers the signed URL should produce.</summary>
/// <param name="FileName">Download file name (Content-Disposition).</param>
/// <param name="ContentType">Content-Type override.</param>
/// <param name="Inline">Content-Disposition inline (true, safe images only) or attachment (false).</param>
public record ReadUrlOptions(string? FileName, string? ContentType, bool Inline);

public sealed record BatchDeleteResult(IReadOnlyCollection<string> Failed)
{
    public static BatchDeleteResult Success { get; } = new(Array.Empty<string>());

    public bool AllSucceeded => Failed.Count == 0;

    public bool Succeeded(string storagePath) => !Failed.Contains(storagePath);
}
