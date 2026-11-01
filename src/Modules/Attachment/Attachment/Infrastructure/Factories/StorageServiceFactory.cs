using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Factories;

/// <summary>
/// Factory for creating storage service instances based on configuration
/// </summary>
public interface IStorageServiceFactory
{
    IStorageService CreateStorageService();
}

/// <summary>
/// Implementation of storage service factory
/// </summary>
public class StorageServiceFactory : IStorageServiceFactory
{
    private readonly IOptions<AttachmentOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StorageServiceFactory> _logger;

    public StorageServiceFactory(
        IOptions<AttachmentOptions> options,
        IServiceProvider serviceProvider,
        ILogger<StorageServiceFactory> logger)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public IStorageService CreateStorageService()
    {
        var provider = _options.Value.Storage.Provider;
        
        _logger.LogInformation("Creating storage service for provider: {Provider}", provider);

        return provider switch
        {
            StorageProvider.Local => CreateLocalStorageService(),
            StorageProvider.AzureBlob => CreateAzureBlobStorageService(),
            StorageProvider.S3 => CreateS3StorageService(),
            _ => throw new ArgumentException($"Unknown storage provider: {provider}")
        };
    }

    private IStorageService CreateLocalStorageService()
    {
        var options = _options.Value.Storage.Local;
        
        _logger.LogInformation(
            "Configuring Local storage: UploadPath={UploadPath}",
            options.UploadPath);

        return _serviceProvider.GetRequiredService<LocalFileStorageService>();
    }

    private IStorageService CreateAzureBlobStorageService()
    {
        var options = _options.Value.Storage.AzureBlob;

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            _logger.LogWarning(
                "Azure Blob Storage connection string is missing. Falling back to Local storage");
            return CreateLocalStorageService();
        }

        _logger.LogInformation(
            "Configuring Azure Blob Storage: Container={ContainerName}",
            options.ContainerName);

        return _serviceProvider.GetRequiredService<AzureBlobStorageService>();
    }

    private IStorageService CreateS3StorageService()
    {
        var options = _options.Value.Storage.S3;

        if (string.IsNullOrWhiteSpace(options.AccessKeyId) || string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            _logger.LogWarning(
                "AWS S3 credentials are missing. Falling back to Local storage");
            return CreateLocalStorageService();
        }

        _logger.LogInformation(
            "Configuring AWS S3 Storage: Bucket={BucketName}, Region={Region}",
            options.BucketName,
            options.Region);

        return _serviceProvider.GetRequiredService<S3StorageService>();
    }
}
