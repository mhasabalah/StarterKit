using Attachment.Contracts.Authorization;
using Attachment.Data.Seed;
using Attachment.Domains.Attachments.Features.DownloadAttachment;
using Attachment.Domains.Attachments.Features.GetPendingAttachmentsBySession;
using Attachment.Infrastructure.Authorization;
using Attachment.Infrastructure.BackgroundJobs;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Factories;
using Attachment.Infrastructure.ImageProcessing;
using Attachment.Infrastructure.Maintenance;
using Attachment.Infrastructure.Security;
using Attachment.Infrastructure.Storage;
using Attachment.Infrastructure.Validation;
using Attachment.Infrastructure.Validation.ClamAv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shared.Core.HealthCheck;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Shared.Core.Extensions;
using Shared.Core.Messaging.Outbox;
using Shared.Core.Mapster;

namespace Attachment;

public static class AttachmentModule
{
    public static IServiceCollection AddAttachmentModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.ConfigureAttachmentOptions(configuration);

        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IPendingAttachmentRepository, PendingAttachmentRepository>();

        services.AddMediator(AttachmentRoot.AssemblyName);
        services.AddCustomMapster(AttachmentRoot.AssemblyName);
        services.AddCarterWithAssemblies(AttachmentRoot.AssemblyName);

        services.AddCustomDbContext<AttachmentDbContext>(configuration, "starterkit");
        services.AddOutbox<AttachmentDbContext>(); // delivers the integration events raised by Attachment aggregates
        services.AddScoped<IDataSeeder, AttachmentDataSeeder>();

        services.AddAttachmentStorage();

        // Security: signing keys, pending sessions, entity access policies.
        services.AddSingleton<IAttachmentSigningKeys, AttachmentSigningKeys>();
        services.AddScoped<IPendingSessionService, PendingSessionService>();
        services.AddScoped<IAttachmentAccessService, AttachmentAccessService>();
        services.AddAttachmentEntityPolicy<UserAttachmentEntityAccessPolicy>(EntityType.User);

        // Validation / processing / URLs.
        services.AddMalwareScanner(configuration);
        services.AddScoped<IImageProcessingService, SkiaImageProcessingService>(); // the image adapter: swap it here
        services.AddScoped<IFileValidationService, FileValidationService>();
        services.AddSingleton<IAttachmentUrls, AttachmentUrls>();
        services.AddScoped<AttachmentReadUrlFactory>();
        services.AddScoped<PendingAttachmentDtoFactory>();

        // Background work: thumbnails, async blob deletion, DB/blob reconciliation.
        services.AddScoped<IAttachmentBackgroundJobs, HangfireAttachmentBackgroundJobs>();
        services.AddScoped<AttachmentFileReaper>();
        services.AddScoped<AttachmentReconciler>();
        services.AddTransient<CleanupExpiredPendingAttachmentsJob>();
        services.AddTransient<GenerateThumbnailJob>();
        services.AddTransient<DeleteAttachmentFilesJob>();
        services.AddTransient<AttachmentReconciliationJob>();
        services.AddHostedService<JobSchedulerBootstrap>();

        return services;
    }

    /// <summary>
    /// <c>Scanner:Provider=ClamAv</c> registers the clamd adapter and a <c>clamav</c> health check (Degraded on
    /// failure, never unready). Otherwise the no-op scanner is a fallback only, so a scanner the host registers
    /// itself wins. <see cref="MalwareScannerStartupCheck"/> enforces the production posture.
    /// </summary>
    private static IServiceCollection AddMalwareScanner(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<MalwareScanMetrics>();
        services.TryAddSingleton(TimeProvider.System); // scan timeout (no-op when the host registered one)

        var provider = configuration
            .GetSection($"{AttachmentOptions.SectionName}:Validation:Scanner")
            .GetValue(nameof(MalwareScannerOptions.Provider), MalwareScannerProvider.None);

        if (provider == MalwareScannerProvider.ClamAv)
        {
            services.TryAddSingleton<ClamAvMalwareScanner>();
            services.AddSingleton<IMalwareScanner>(sp => sp.GetRequiredService<ClamAvMalwareScanner>());
            services.AddHealthChecks().AddCheck<ClamAvHealthCheck>(
                ClamAvHealthCheck.Name,
                failureStatus: HealthStatus.Degraded,
                tags: [HealthCheckTags.Ready]);
        }
        else
        {
            services.TryAddSingleton<IMalwareScanner, NoOpMalwareScanner>();
        }

        services.AddHostedService<MalwareScannerStartupCheck>();
        return services;
    }

    private static IServiceCollection ConfigureAttachmentOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<AttachmentOptions>, AttachmentOptionsValidator>();

        // Bind once (binding twice appends array items). Defaults for lists are applied only when not configured.
        services.AddOptions<AttachmentOptions>()
            .Bind(configuration.GetSection(AttachmentOptions.SectionName))
            .PostConfigure(o => o.ApplyDefaults())
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Runs migrations. Uploaded files are deliberately NOT exposed as static files: every read goes through
    /// <c>GET /attachments/{id}/download</c> (access-checked) or a short-lived signed URL.
    /// </summary>
    public static IApplicationBuilder UseAttachmentModule(this IApplicationBuilder app)
    {
        app.UseMigration<AttachmentDbContext>();
        return app;
    }
}
