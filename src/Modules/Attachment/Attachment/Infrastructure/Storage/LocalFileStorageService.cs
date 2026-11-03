using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Storage;

/// <summary>
/// Stores files on the local file system under <c>Attachment:Storage:Local:UploadPath</c>.
/// Works the same on Windows and Linux: storage paths are '/'-separated keys, and every path goes through
/// <see cref="ResolveFullPath"/>, which rejects anything that would escape the upload root.
/// Files are never served as static files; reads go through the download endpoint or a signed URL.
/// </summary>
public sealed class LocalFileStorageService : IStorageService, IStorageListing
{
    private readonly ILocalReadUrlSigner _signer;
    private readonly ILogger<LocalFileStorageService> _logger;
    private readonly string _root;
    private readonly StringComparison _pathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public LocalFileStorageService(
        IHostEnvironment environment,
        IOptions<AttachmentOptions> options,
        ILocalReadUrlSigner signer,
        ILogger<LocalFileStorageService> logger)
    {
        _signer = signer;
        _logger = logger;

        var configured = options.Value.Storage.Local.UploadPath;
        var root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured);
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
    }

    /// <summary>Absolute upload root, always ending with a directory separator.</summary>
    public string UploadRoot => _root;

    public bool ServesThroughApplication => true;

    /// <summary>
    /// Maps a '/'-separated storage key to an absolute path inside the upload root.
    /// Throws <see cref="InvalidStoragePathException"/> for traversal attempts or malformed keys.
    /// </summary>
    public string ResolveFullPath(string storagePath)
    {
        var segments = StoragePaths.ValidateAndSplit(storagePath);
        var fullPath = Path.GetFullPath(Path.Combine([_root, .. segments]));

        if (!fullPath.StartsWith(_root, _pathComparison) || fullPath.Length == _root.Length)
            throw new InvalidStoragePathException(storagePath);

        return fullPath;
    }

    public async Task<StorageResult> UploadToPathAsync(
        Stream fileStream,
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(storagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Write to a temp file and rename, so a crashed upload never leaves a half-written file at the key.
        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var destination = new FileStream(
                             tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await fileStream.CopyToAsync(destination, cancellationToken);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteFile(tempPath);
            _logger.LogError(ex, "Error writing file to local storage: {StoragePath}", storagePath);
            throw new FileStorageException("Failed to store the file.", ex);
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }

        var size = new FileInfo(fullPath).Length;
        _logger.LogDebug("File stored locally: {StoragePath} ({Size} bytes)", storagePath, size);
        return new StorageResult(storagePath, size);
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(storagePath);
        if (!File.Exists(fullPath))
            throw new StoredFileNotFoundException(storagePath);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveFullPath(storagePath);
        try
        {
            File.Delete(fullPath); // no-op when missing
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Error deleting file from local storage: {StoragePath}", storagePath);
            throw new FileStorageException("Failed to delete the file.", ex);
        }

        return Task.CompletedTask;
    }

    public async Task<BatchDeleteResult> DeleteBatchAsync(
        IEnumerable<string> storagePaths,
        CancellationToken cancellationToken = default)
    {
        var failed = new List<string>();
        foreach (var path in storagePaths.Distinct())
        {
            try
            {
                await DeleteAsync(path, cancellationToken);
            }
            catch (Exception ex) when (ex is FileStorageException or InvalidStoragePathException)
            {
                _logger.LogWarning(ex, "Could not delete {StoragePath} from local storage", path);
                failed.Add(path);
            }
        }

        return failed.Count == 0 ? BatchDeleteResult.Success : new BatchDeleteResult(failed);
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(ResolveFullPath(storagePath)));

    public Task<StorageResult> CopyAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default)
    {
        var source = ResolveFullPath(sourceStoragePath);
        var destination = ResolveFullPath(destinationStoragePath);

        if (!File.Exists(source))
            throw new StoredFileNotFoundException(sourceStoragePath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            TouchForOrphanScan(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Error copying {Source} to {Destination}", sourceStoragePath, destinationStoragePath);
            throw new FileStorageException("Failed to copy the file.", ex);
        }

        return Task.FromResult(new StorageResult(destinationStoragePath, new FileInfo(destination).Length));
    }

    public Task<StorageResult> MoveAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default)
    {
        var source = ResolveFullPath(sourceStoragePath);
        var destination = ResolveFullPath(destinationStoragePath);

        if (!File.Exists(source))
            throw new StoredFileNotFoundException(sourceStoragePath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, overwrite: true);
            TouchForOrphanScan(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Error moving {Source} to {Destination}", sourceStoragePath, destinationStoragePath);
            throw new FileStorageException("Failed to move the file.", ex);
        }

        return Task.FromResult(new StorageResult(destinationStoragePath, new FileInfo(destination).Length));
    }

    public Task<string> GetReadUrlAsync(
        string storagePath,
        TimeSpan ttl,
        ReadUrlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(storagePath);
        return Task.FromResult(_signer.CreateUrl(storagePath, DateTimeOffset.UtcNow.Add(ttl), options));
    }

    public async IAsyncEnumerable<StoredObjectInfo> ListAsync(string? prefix = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Prefixes used by the app are folders (tenant roots end with '/'): list that folder only.
        var start = string.IsNullOrEmpty(prefix)
            ? _root
            : Path.Combine(_root, StoragePaths.Validate(prefix.TrimEnd('/')).Replace('/', Path.DirectorySeparatorChar));

        if (!Directory.Exists(start))
            yield break;

        foreach (var fullPath in Directory.EnumerateFiles(start, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(_root, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            DateTime lastWrite;
            try
            {
                lastWrite = File.GetLastWriteTimeUtc(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // deleted while enumerating
            }

            yield return new StoredObjectInfo(relative, new DateTimeOffset(lastWrite, TimeSpan.Zero));
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// File.Move/Copy keep the source's modification time. A moved file is new at its destination (its row is
    /// saved right after), so reset the time: the orphan scan's safety window must protect it.
    /// </summary>
    private void TouchForOrphanScan(string fullPath)
    {
        try
        {
            File.SetLastWriteTimeUtc(fullPath, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not update the modification time of {Path}", fullPath);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete temp file {Path}", path);
        }
    }
}
