using Attachment.Domains.Attachments.Exceptions;
using Attachment.Infrastructure.Configuration;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Storage;

/// <summary>
/// Azure Blob storage. The container is private; reads use short-lived read-only SAS URLs
/// (account-key SAS when the client has a shared key, otherwise a user-delegation SAS).
/// The container is created once per process at startup (<see cref="AzureBlobContainerInitializer"/>), not per upload.
/// </summary>
public sealed class AzureBlobStorageService(
    BlobServiceClient blobServiceClient,
    AzureBlobContainerGate containerGate,
    ILogger<AzureBlobStorageService> logger) : IStorageService, IStorageListing
{
    public bool ServesThroughApplication => false;

    private BlobContainerClient Container => containerGate.Container;

    public async Task<StorageResult> UploadToPathAsync(
        Stream fileStream,
        string storagePath,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(storagePath);
        var knownLength = StorageStreams.RemainingLength(fileStream);
        try
        {
            await containerGate.EnsureCreatedAsync(cancellationToken); // no-op after the first success
            var blob = Container.GetBlobClient(storagePath);
            await blob.UploadAsync(fileStream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            }, cancellationToken);

            // The upload length is known for seekable streams (every upload path): no extra properties request.
            if (knownLength is { } length)
                return new StorageResult(storagePath, length);

            var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken);
            return new StorageResult(storagePath, properties.Value.ContentLength);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Azure error uploading {Path} ({ErrorCode})", storagePath, ex.ErrorCode);
            throw new FileStorageException("Failed to store the file.", ex);
        }
    }

    public async Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(storagePath);
        try
        {
            return await Container.GetBlobClient(storagePath).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new StoredFileNotFoundException(storagePath);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Azure error reading {Path} ({ErrorCode})", storagePath, ex.ErrorCode);
            throw new FileStorageException("Failed to read the file.", ex);
        }
    }

    public async Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(storagePath);
        try
        {
            await Container.GetBlobClient(storagePath).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Azure error deleting {Path} ({ErrorCode})", storagePath, ex.ErrorCode);
            throw new FileStorageException("Failed to delete the file.", ex);
        }
    }

    public async Task<BatchDeleteResult> DeleteBatchAsync(
        IEnumerable<string> storagePaths,
        CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(storagePaths.Distinct().Select(async path =>
        {
            try
            {
                await DeleteAsync(path, cancellationToken);
                return null;
            }
            catch (Exception ex) when (ex is FileStorageException or InvalidStoragePathException)
            {
                return path;
            }
        }));

        var failed = results.Where(p => p is not null).Select(p => p!).ToList();
        return failed.Count == 0 ? BatchDeleteResult.Success : new BatchDeleteResult(failed);
    }

    public async Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(storagePath);
        return await Container.GetBlobClient(storagePath).ExistsAsync(cancellationToken);
    }

    public async Task<StorageResult> CopyAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(sourceStoragePath);
        StoragePaths.Validate(destinationStoragePath);
        try
        {
            var source = Container.GetBlobClient(sourceStoragePath);
            var destination = Container.GetBlobClient(destinationStoragePath);

            // The container is private, so the copy source must carry a short read SAS.
            var sourceUri = await CreateReadSasUriAsync(source, TimeSpan.FromMinutes(15), null, cancellationToken);
            var operation = await destination.StartCopyFromUriAsync(sourceUri, cancellationToken: cancellationToken);
            await operation.WaitForCompletionAsync(cancellationToken);

            var properties = await destination.GetPropertiesAsync(cancellationToken: cancellationToken);
            return new StorageResult(destinationStoragePath, properties.Value.ContentLength);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new StoredFileNotFoundException(sourceStoragePath);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Azure error copying {Source} to {Destination}", sourceStoragePath, destinationStoragePath);
            throw new FileStorageException("Failed to copy the file.", ex);
        }
    }

    public async Task<StorageResult> MoveAsync(
        string sourceStoragePath,
        string destinationStoragePath,
        CancellationToken cancellationToken = default)
    {
        var result = await CopyAsync(sourceStoragePath, destinationStoragePath, cancellationToken);
        await DeleteAsync(sourceStoragePath, cancellationToken);
        return result;
    }

    public async Task<string> GetReadUrlAsync(
        string storagePath,
        TimeSpan ttl,
        ReadUrlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        StoragePaths.Validate(storagePath);
        var uri = await CreateReadSasUriAsync(Container.GetBlobClient(storagePath), ttl, options, cancellationToken);
        return uri.ToString();
    }

    private async Task<Uri> CreateReadSasUriAsync(
        BlobClient blob, TimeSpan ttl, ReadUrlOptions? options, CancellationToken cancellationToken)
    {
        var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5); // clock skew
        var expiresOn = DateTimeOffset.UtcNow.Add(ttl);

        var sas = new BlobSasBuilder
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
            StartsOn = startsOn,
            ExpiresOn = expiresOn,
            Protocol = SasProtocol.Https,
            ContentDisposition = FileResponseHeaders.ContentDisposition(options?.FileName, options?.Inline == true)
        };

        if (!string.IsNullOrEmpty(options?.ContentType))
            sas.ContentType = options.ContentType;

        sas.SetPermissions(BlobSasPermissions.Read);

        if (blob.CanGenerateSasUri)
            return blob.GenerateSasUri(sas);

        // Token credential (managed identity): sign with a user delegation key.
        var key = await blobServiceClient.GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken);
        return new BlobUriBuilder(blob.Uri)
        {
            Sas = sas.ToSasQueryParameters(key.Value, blobServiceClient.AccountName)
        }.ToUri();
    }

    public async IAsyncEnumerable<StoredObjectInfo> ListAsync(string? prefix = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in Container.GetBlobsAsync(Azure.Storage.Blobs.Models.BlobTraits.None, Azure.Storage.Blobs.Models.BlobStates.None, prefix, cancellationToken))
        {
            // Unknown time = treat as brand new, so the orphan scan never deletes it.
            yield return new StoredObjectInfo(item.Name, item.Properties.LastModified ?? DateTimeOffset.UtcNow);
        }
    }
}

/// <summary>
/// Process-wide (singleton) owner of the container client. <see cref="EnsureCreatedAsync"/> creates the private
/// container once; afterwards it is a no-op without any network call.
/// </summary>
public sealed class AzureBlobContainerGate(BlobServiceClient blobServiceClient, IOptions<AttachmentOptions> options)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile bool _ensured;

    public BlobContainerClient Container { get; } =
        blobServiceClient.GetBlobContainerClient(options.Value.Storage.AzureBlob.ContainerName);

    public bool IsEnsured => _ensured;

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_ensured) return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_ensured) return;
            await Container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
            _ensured = true;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>
/// Creates the Azure container at startup (only when Azure is the selected provider). A failure is logged, not
/// fatal: the first upload tries again through <see cref="AzureBlobContainerGate"/>.
/// </summary>
public sealed class AzureBlobContainerInitializer(
    IServiceProvider services,
    IOptions<AttachmentOptions> options,
    ILogger<AzureBlobContainerInitializer> logger) : Microsoft.Extensions.Hosting.IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.Storage.Provider != StorageProvider.AzureBlob)
            return;

        try
        {
            await services.GetRequiredService<AzureBlobContainerGate>().EnsureCreatedAsync(cancellationToken);
            logger.LogInformation("Azure attachment container {Container} is ready",
                options.Value.Storage.AzureBlob.ContainerName);
        }
        catch (Exception ex) when (ex is RequestFailedException or HttpRequestException)
        {
            logger.LogWarning(ex, "Could not create the Azure attachment container at startup; the first upload retries");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
