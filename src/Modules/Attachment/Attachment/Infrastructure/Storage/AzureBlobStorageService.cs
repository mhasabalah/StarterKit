using Attachment.Infrastructure.Configuration;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Storage;

public class AzureBlobStorageService : IStorageService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly IOptions<AttachmentOptions> _options;
    private readonly ILogger<AzureBlobStorageService> _logger;

    public AzureBlobStorageService(
        BlobServiceClient blobServiceClient,
        IOptions<AttachmentOptions> options,
        ILogger<AzureBlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
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
            var azureOptions = _options.Value.Storage.AzureBlob;
            
            // Get or create container
            var containerClient = _blobServiceClient.GetBlobContainerClient(azureOptions.ContainerName);
            
            var publicAccessType = azureOptions.UsePublicAccess 
                ? PublicAccessType.Blob 
                : PublicAccessType.None;
                
            await containerClient.CreateIfNotExistsAsync(
                publicAccessType, 
                cancellationToken: cancellationToken);

            // Create blob path: yyyy/MM/dd/guid_filename
            var now = DateTime.UtcNow;
            var blobPath = $"{now:yyyy}/{now:MM}/{now:dd}/{Guid.NewGuid()}_{Path.GetFileName(fileName)}";

            // Get blob client
            var blobClient = containerClient.GetBlobClient(blobPath);

            // Set content type
            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            };

            // Upload blob
            await blobClient.UploadAsync(
                fileStream,
                new BlobUploadOptions
                {
                    HttpHeaders = blobHttpHeaders
                },
                cancellationToken);

            // Get properties for file size
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            // Generate public URL
            var publicUrl = blobClient.Uri.ToString();

            _logger.LogInformation(
                "File uploaded to Azure Blob Storage: Container={Container}, Path={BlobPath}, Size={FileSize} bytes",
                azureOptions.ContainerName,
                blobPath,
                properties.Value.ContentLength);

            return new StorageResult(blobPath, publicUrl, properties.Value.ContentLength);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to Azure Blob Storage: {FileName}", fileName);
            throw new InvalidOperationException($"Failed to upload file to Azure Blob Storage: {fileName}", ex);
        }
    }

    public async Task<Stream> DownloadAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var azureOptions = _options.Value.Storage.AzureBlob;
            var containerClient = _blobServiceClient.GetBlobContainerClient(azureOptions.ContainerName);
            var blobClient = containerClient.GetBlobClient(storagePath);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                throw new FileNotFoundException($"Blob not found: {storagePath}");
            }

            var downloadResponse = await blobClient.DownloadAsync(cancellationToken);
            
            var memoryStream = new MemoryStream();
            await downloadResponse.Value.Content.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;

            _logger.LogInformation("File downloaded from Azure Blob Storage: {BlobPath}", storagePath);

            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file from Azure Blob Storage: {StoragePath}", storagePath);
            throw;
        }
    }

    public async Task DeleteAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var azureOptions = _options.Value.Storage.AzureBlob;
            var containerClient = _blobServiceClient.GetBlobContainerClient(azureOptions.ContainerName);
            var blobClient = containerClient.GetBlobClient(storagePath);

            await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

            _logger.LogInformation("File deleted from Azure Blob Storage: {BlobPath}", storagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from Azure Blob Storage: {StoragePath}", storagePath);
            throw;
        }
    }

    public Task<string> GetPublicUrlAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        var azureOptions = _options.Value.Storage.AzureBlob;
        var containerClient = _blobServiceClient.GetBlobContainerClient(azureOptions.ContainerName);
        var blobClient = containerClient.GetBlobClient(storagePath);
        
        var publicUrl = blobClient.Uri.ToString();
        return Task.FromResult(publicUrl);
    }

    public async Task<bool> ExistsAsync(
        string storagePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var azureOptions = _options.Value.Storage.AzureBlob;
            var containerClient = _blobServiceClient.GetBlobContainerClient(azureOptions.ContainerName);
            var blobClient = containerClient.GetBlobClient(storagePath);

            return await blobClient.ExistsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if file exists in Azure Blob Storage: {StoragePath}", storagePath);
            return false;
        }
    }

    public async Task<StorageResult> UploadToPathAsync(
        Stream fileStream,
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var azureOptions = _options.Value.Storage.AzureBlob;
            var containerClient = _blobServiceClient.GetBlobContainerClient(azureOptions.ContainerName);

            var publicAccessType = azureOptions.UsePublicAccess
                ? PublicAccessType.Blob
                : PublicAccessType.None;

            await containerClient.CreateIfNotExistsAsync(
                publicAccessType,
                cancellationToken: cancellationToken);

            var blobClient = containerClient.GetBlobClient(storagePath);

            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            };

            await blobClient.UploadAsync(
                fileStream,
                new BlobUploadOptions
                {
                    HttpHeaders = blobHttpHeaders
                },
                cancellationToken);

            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var publicUrl = blobClient.Uri.ToString();

            _logger.LogInformation(
                "File uploaded to specific path in Azure Blob Storage: Path={BlobPath}, Size={FileSize} bytes",
                storagePath,
                properties.Value.ContentLength);

            return new StorageResult(storagePath, publicUrl, properties.Value.ContentLength);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to path in Azure Blob Storage: {StoragePath}", storagePath);
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
            var azureOptions = _options.Value.Storage.AzureBlob;
            var containerClient = _blobServiceClient.GetBlobContainerClient(azureOptions.ContainerName);

            var sourceBlobClient = containerClient.GetBlobClient(sourceStoragePath);
            var destinationBlobClient = containerClient.GetBlobClient(destinationStoragePath);

            if (!await sourceBlobClient.ExistsAsync(cancellationToken))
            {
                throw new FileNotFoundException($"Source blob not found: {sourceStoragePath}");
            }

            // Server-side copy using Azure SDK
            var copyOperation = await destinationBlobClient.StartCopyFromUriAsync(
                sourceBlobClient.Uri,
                cancellationToken: cancellationToken);

            // Wait for copy to complete
            await copyOperation.WaitForCompletionAsync(cancellationToken);

            var properties = await destinationBlobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var publicUrl = destinationBlobClient.Uri.ToString();

            _logger.LogInformation(
                "File copied in Azure Blob Storage: Source={Source}, Destination={Destination}",
                sourceStoragePath,
                destinationStoragePath);

            return new StorageResult(destinationStoragePath, publicUrl, properties.Value.ContentLength);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error copying file in Azure Blob Storage: Source={Source}, Destination={Destination}",
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
            // Copy the file
            var result = await CopyAsync(sourceStoragePath, destinationStoragePath, cancellationToken);

            // Delete the source
            await DeleteAsync(sourceStoragePath, cancellationToken);

            _logger.LogInformation(
                "File moved in Azure Blob Storage: Source={Source}, Destination={Destination}",
                sourceStoragePath,
                destinationStoragePath);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error moving file in Azure Blob Storage: Source={Source}, Destination={Destination}",
                sourceStoragePath,
                destinationStoragePath);
            throw;
        }
    }

    public async Task DeleteBatchAsync(
        IEnumerable<string> storagePaths,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var azureOptions = _options.Value.Storage.AzureBlob;
            var containerClient = _blobServiceClient.GetBlobContainerClient(azureOptions.ContainerName);

            var deleteTasks = storagePaths
                .Select(path => containerClient.GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken));

            await Task.WhenAll(deleteTasks);

            _logger.LogInformation("Batch deleted {Count} files from Azure Blob Storage", storagePaths.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error batch deleting files from Azure Blob Storage");
            throw;
        }
    }
}
