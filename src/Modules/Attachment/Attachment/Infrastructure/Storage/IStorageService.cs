namespace Attachment.Infrastructure.Storage;

public interface IStorageService
{
    Task<StorageResult> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream> DownloadAsync(
        string storagePath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storagePath,
        CancellationToken cancellationToken = default);

    Task<string> GetPublicUrlAsync(
        string storagePath,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string storagePath,
        CancellationToken cancellationToken = default);

    Task<StorageResult> UploadToPathAsync(
        Stream fileStream,
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<StorageResult> CopyAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default);

    Task<StorageResult> MoveAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default);

    Task DeleteBatchAsync(
        IEnumerable<string> storagePaths,
        CancellationToken cancellationToken = default);
}

public record StorageResult(
    string StoragePath,
    string PublicUrl,
    long FileSize);
