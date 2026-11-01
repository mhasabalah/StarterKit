using Amazon.S3;
using Attachment.Data.Seed;
using Attachment.Infrastructure.Authorization;
using Attachment.Infrastructure.BackgroundJobs;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Factories;
using Attachment.Infrastructure.ImageProcessing;
using Attachment.Infrastructure.Storage;
using Attachment.Infrastructure.Validation;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Core.Extensions;
using Shared.Core.Mapster;

namespace Attachment;

public static class AttachmentModule
{
    public static IServiceCollection AddAttachmentModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure Options Pattern
        services.ConfigureAttachmentOptions(configuration);

        // Register repositories
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IPendingAttachmentRepository, PendingAttachmentRepository>();

        // Register MediatR, Mapster, Carter
        services.AddMediatR(AttachmentRoot.AssemblyName);
        services.AddCustomMapster(AttachmentRoot.AssemblyName);
        services.AddCarterWithAssemblies(AttachmentRoot.AssemblyName);

        // Register DbContext
        services.AddCustomDbContext<AttachmentDbContext>(configuration, "starterkit");
        services.AddScoped<IDataSeeder, AttachmentDataSeeder>();

        // Register storage services
        services.ConfigureStorageServices();

        // Register other services
        services.AddScoped<IImageProcessingService, ImageProcessingService>();
        services.AddScoped<IFileValidationService, FileValidationService>();
        services.AddScoped<IAttachmentAuthorizationService, AttachmentAuthorizationService>();

        // Register background jobs
        services.AddTransient<CleanupExpiredPendingAttachmentsJob>();
        services.AddTransient<GenerateThumbnailJob>();

        // Register job scheduler bootstrap
        services.AddHostedService<JobSchedulerBootstrap>();

        return services;
    }

    /// <summary>
    /// Configures AttachmentOptions using the Options pattern with validation
    /// </summary>
    private static IServiceCollection ConfigureAttachmentOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration section
        services.Configure<AttachmentOptions>(
            configuration.GetSection(AttachmentOptions.SectionName));

        // Add options validation
        services.AddSingleton<IValidateOptions<AttachmentOptions>, AttachmentOptionsValidator>();

        // Validate options on startup
        services.AddOptions<AttachmentOptions>()
            .Bind(configuration.GetSection(AttachmentOptions.SectionName))
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Configures storage services based on the configured provider
    /// </summary>
    private static IServiceCollection ConfigureStorageServices(this IServiceCollection services)
    {
        // Register storage implementations
        services.AddScoped<LocalFileStorageService>();
        services.AddScoped<AzureBlobStorageService>();
        services.AddScoped<S3StorageService>();

        // Register Azure Blob Service Client (if needed)
        services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AttachmentOptions>>().Value;

            if (options.Storage.Provider == StorageProvider.AzureBlob &&
                !string.IsNullOrWhiteSpace(options.Storage.AzureBlob.ConnectionString))
            {
                return new BlobServiceClient(options.Storage.AzureBlob.ConnectionString);
            }

            // Return null client if not using Azure
            return null!;
        });

        // Register AWS S3 Client (if needed)
        services.AddSingleton<IAmazonS3>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AttachmentOptions>>().Value;

            if (options.Storage.Provider == StorageProvider.S3 &&
                !string.IsNullOrWhiteSpace(options.Storage.S3.AccessKeyId) &&
                !string.IsNullOrWhiteSpace(options.Storage.S3.SecretAccessKey))
            {
                var config = new Amazon.S3.AmazonS3Config
                {
                    RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Storage.S3.Region)
                };

                return new AmazonS3Client(
                    options.Storage.S3.AccessKeyId,
                    options.Storage.S3.SecretAccessKey,
                    config);
            }

            // Return null client if not using S3
            return null!;
        });

        // Register storage factory
        services.AddScoped<IStorageServiceFactory, StorageServiceFactory>();

        // Register IStorageService using factory
        services.AddScoped<IStorageService>(serviceProvider =>
        {
            var factory = serviceProvider.GetRequiredService<IStorageServiceFactory>();
            return factory.CreateStorageService();
        });

        return services;
    }

    public static IApplicationBuilder UseAttachmentModule(this IApplicationBuilder app)
    {
        app.UseMigration<AttachmentDbContext>();

        // Configure static files for local storage
        var options = app.ApplicationServices
            .GetRequiredService<IOptions<AttachmentOptions>>()
            .Value;
        
        if (options.Storage.Provider == StorageProvider.Local)
        {
            var uploadPath = options.Storage.Local.UploadPath;
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), uploadPath);
            
            // Ensure directory exists
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
            
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(fullPath),
                RequestPath = $"/{uploadPath}"
            });
        }

        return app;
    }
}
