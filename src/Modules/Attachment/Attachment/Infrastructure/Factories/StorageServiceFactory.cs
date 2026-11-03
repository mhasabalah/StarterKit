using Amazon;
using Amazon.S3;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Attachment.Infrastructure.Factories;

/// <summary>
/// Registers the storage provider selected by <c>Attachment:Storage:Provider</c>. There is no fallback:
/// a missing provider configuration fails <c>ValidateOnStart</c> (see <see cref="AttachmentOptionsValidator"/>),
/// and the provider clients are only created for the selected provider.
/// </summary>
public static class StorageServiceFactory
{
    public static IServiceCollection AddAttachmentStorage(this IServiceCollection services)
    {
        services.AddSingleton<ILocalReadUrlSigner, LocalReadUrlSigner>();

        services.AddScoped<LocalFileStorageService>();
        services.AddScoped<AzureBlobStorageService>();
        services.AddScoped<S3StorageService>();

        services.AddSingleton(sp => CreateBlobServiceClient(Options(sp)));
        services.AddSingleton<AzureBlobContainerGate>();
        services.AddHostedService<AzureBlobContainerInitializer>(); // creates the container once, at startup
        services.AddSingleton<IAmazonS3>(sp => CreateS3Client(Options(sp)));

        services.AddScoped<IStorageService>(sp => Options(sp).Storage.Provider switch
        {
            StorageProvider.Local => sp.GetRequiredService<LocalFileStorageService>(),
            StorageProvider.AzureBlob => sp.GetRequiredService<AzureBlobStorageService>(),
            StorageProvider.S3 => sp.GetRequiredService<S3StorageService>(),
            var other => throw new InvalidOperationException($"Unknown Attachment storage provider '{other}'.")
        });

        return services;
    }

    private static AttachmentOptions Options(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<AttachmentOptions>>().Value;

    private static BlobServiceClient CreateBlobServiceClient(AttachmentOptions options)
    {
        var connectionString = options.Storage.AzureBlob.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Attachment:Storage:AzureBlob:ConnectionString is not configured.");

        return new BlobServiceClient(connectionString);
    }

    /// <summary>
    /// Static keys when both AccessKeyId and SecretAccessKey are set; otherwise the AWS default credential
    /// chain (environment, shared profile, ECS task role, EC2 instance profile / IAM role).
    /// </summary>
    public static IAmazonS3 CreateS3Client(AttachmentOptions options)
    {
        var s3 = options.Storage.S3;
        var config = new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(s3.Region) };

        return string.IsNullOrWhiteSpace(s3.AccessKeyId) || string.IsNullOrWhiteSpace(s3.SecretAccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(s3.AccessKeyId, s3.SecretAccessKey, config);
    }
}
