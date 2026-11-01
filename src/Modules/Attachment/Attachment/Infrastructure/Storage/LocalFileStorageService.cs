using Attachment.Infrastructure.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Storage;

public class LocalFileStorageService : IStorageService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IOptions<AttachmentOptions> _options;
    private readonly ILogger<LocalFileStorageService> _logger;
    private string UploadPath => Path.Combine(_environment.ContentRootPath, _options.Value.Storage.Local.UploadPath);

    public LocalFileStorageService(
        IWebHostEnvironment environment,
        IOptions<AttachmentOptions> options,
        ILogger<LocalFileStorageService> logger)
    {
        _environment = environment;
        _options = options;
        _logger = logger;
    }

    public async Task<StorageResult> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create directory structure: uploads/yyyy/MM/dd
            var now = DateTime.UtcNow;
            var relativePath = Path.Combine(
                now.Year.ToString(),
                now.Month.ToString("D2"),
                now.Day.ToString("D2"));

            var fullDirectoryPath = Path.Combine(UploadPath, relativePath);
            Directory.CreateDirectory(fullDirectoryPath);

            // Generate unique filename
            var uniqueFileName = $"{Guid.NewGuid()}_{Path.GetFileName(fileName)}";
            var fullFilePath = Path.Combine(fullDirectoryPath, uniqueFileName);

            // Save file
            await using var fileStreamDest = new FileStream(fullFilePath, FileMode.Create, FileAccess.Write);
            await fileStream.CopyToAsync(fileStreamDest, cancellationToken);

            var fileInfo = new FileInfo(fullFilePath);
            var storagePath = Path.Combine(relativePath, uniqueFileName).Replace("\\", "/");
            
            // Use configured base URL or default
            var baseUrl = _options.Value.Storage.Local.BaseUrl ?? $"/{_options.Value.Storage.Local.UploadPath}";
            var publicUrl = $"{baseUrl}/{storagePath}";

            _logger.LogInformation(
                "File uploaded to local storage: {StoragePath}, Size: {FileSize} bytes",
                storagePath,
                fileInfo.Length);

            return new StorageResult(storagePath, publicUrl, fileInfo.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to local storage: {FileName}", fileName);
            throw new InvalidOperationException($"Failed to upload file: {fileName}", ex);
        }
    }

    public async Task<Stream> DownloadAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fullFilePath = Path.Combine(UploadPath, storagePath.Replace("/", "\\"));

            if (!File.Exists(fullFilePath))
            {
                throw new FileNotFoundException($"File not found: {storagePath}");
            }

            var memory = new MemoryStream();
            await using var fileStream = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read);
            await fileStream.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;

            _logger.LogInformation("File downloaded from local storage: {StoragePath}", storagePath);

            return memory;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from local storage: {StoragePath}", storagePath);
            throw;
        }
    }

    public Task DeleteAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fullFilePath = Path.Combine(UploadPath, storagePath.Replace("/", "\\"));

            if (File.Exists(fullFilePath))
            {
                File.Delete(fullFilePath);
                _logger.LogInformation("File deleted from local storage: {StoragePath}", storagePath);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from local storage: {StoragePath}", storagePath);
            throw;
        }
    }

    public Task<string> GetPublicUrlAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        var publicUrl = $"/uploads/{storagePath}";
        return Task.FromResult(publicUrl);
    }

    public Task<bool> ExistsAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        var fullFilePath = Path.Combine(UploadPath, storagePath.Replace("/", "\\"));
        return Task.FromResult(File.Exists(fullFilePath));
    }

    public async Task<StorageResult> UploadToPathAsync(
        Stream fileStream,
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fullFilePath = Path.Combine(UploadPath, storagePath.Replace("/", "\\"));
            var directory = Path.GetDirectoryName(fullFilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var fileStreamDest = new FileStream(fullFilePath, FileMode.Create, FileAccess.Write);
            await fileStream.CopyToAsync(fileStreamDest, cancellationToken);

            var fileInfo = new FileInfo(fullFilePath);
            var baseUrl = _options.Value.Storage.Local.BaseUrl ?? $"/{_options.Value.Storage.Local.UploadPath}";
            var publicUrl = $"{baseUrl}/{storagePath}";

            _logger.LogInformation(
                "File uploaded to specific path in local storage: {StoragePath}, Size: {FileSize} bytes",
                storagePath,
                fileInfo.Length);

            return new StorageResult(storagePath, publicUrl, fileInfo.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to path in local storage: {StoragePath}", storagePath);
            throw new InvalidOperationException($"Failed to upload file to path: {storagePath}", ex);
        }
    }

    public async Task<StorageResult> CopyAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sourceFullPath = Path.Combine(UploadPath, sourceStoragePath.Replace("/", "\\"));
            var destinationFullPath = Path.Combine(UploadPath, destinationStoragePath.Replace("/", "\\"));

            if (!File.Exists(sourceFullPath))
            {
                throw new FileNotFoundException($"Source file not found: {sourceStoragePath}");
            }

            var directory = Path.GetDirectoryName(destinationFullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(sourceFullPath, destinationFullPath, overwrite: true);

            var fileInfo = new FileInfo(destinationFullPath);
            var baseUrl = _options.Value.Storage.Local.BaseUrl ?? $"/{_options.Value.Storage.Local.UploadPath}";
            var publicUrl = $"{baseUrl}/{destinationStoragePath}";

            _logger.LogInformation(
                "File copied in local storage: Source={Source}, Destination={Destination}",
                sourceStoragePath,
                destinationStoragePath);

            return await Task.FromResult(new StorageResult(destinationStoragePath, publicUrl, fileInfo.Length));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error copying file in local storage: Source={Source}, Destination={Destination}",
                sourceStoragePath,
                destinationStoragePath);
            throw;
        }
    }

    public async Task<StorageResult> MoveAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await CopyAsync(sourceStoragePath, destinationStoragePath, cancellationToken);
            await DeleteAsync(sourceStoragePath, cancellationToken);

            _logger.LogInformation(
                "File moved in local storage: Source={Source}, Destination={Destination}",
                sourceStoragePath,
                destinationStoragePath);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error moving file in local storage: Source={Source}, Destination={Destination}",
                sourceStoragePath,
                destinationStoragePath);
            throw;
        }
    }

    public Task DeleteBatchAsync(
        IEnumerable<string> storagePaths,
        CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var storagePath in storagePaths)
            {
                var fullFilePath = Path.Combine(UploadPath, storagePath.Replace("/", "\\"));
                if (File.Exists(fullFilePath))
                {
                    File.Delete(fullFilePath);
                }
            }

            _logger.LogInformation("Batch deleted {Count} files from local storage", storagePaths.Count());
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error batch deleting files from local storage");
            throw;
        }
    }
}
