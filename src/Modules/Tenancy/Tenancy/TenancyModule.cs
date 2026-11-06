using DataTransfer.Contracts.Definitions;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.Mapster;
using Shared.Contracts.FeatureFlags;
using Shared.Core.FeatureFlags;
using Subscriptions.Contracts.Subscribers;
using Tenancy.Data.Seed;
using Tenancy.Domains.FeatureFlags.Services;
using Tenancy.Domains.Members.DataTransfer;
using Tenancy.Domains.Members.Features;
using Tenancy.Jobs;
using Tenancy.Services;

namespace Tenancy;

/// <summary>
/// Tenancy module: tenants (companies), branch trees, memberships of global users, tenant roles, invitations and the
/// tenant audit trail - plus the implementations of the kernel's <see cref="ITenantStore"/> and
/// <see cref="ITenantMembershipResolver"/>.
/// <para>Works in both modes: single-tenant (the Default tenant, its branches and roles) and multi-tenant.</para>
/// Program.cs: <c>AddMultiTenancy</c> first; register this module right after Identity; add <c>TenancyRoot</c> to
/// <c>AddMassTransitWithAssemblies</c>.
/// </summary>
public static class TenancyModule
{
    public const string PurgeJobId = "tenancy-purge-deleted-tenants";

    public static IServiceCollection AddTenancyModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCustomDbContext<TenancyDbContext>(configuration, "starterkit");
        services.AddScoped<IDataSeeder, TenancyDataSeeder>();

        // Kernel implementations (replace the single-tenant defaults).
        services.AddSingleton<TenantStore>();
        services.Replace(ServiceDescriptor.Singleton<ITenantStore>(sp => sp.GetRequiredService<TenantStore>()));
        services.Replace(ServiceDescriptor.Singleton<ITenantMembershipResolver, TenantMembershipResolver>());

        // Runtime feature flags: stored in the host database, cached (L1 + distributed, evicted over the invalidation bus).
        // Replaces the configuration-only default registered by AddFeatureFlags (AddScoped wins regardless of order).
        services.AddFeatureFlags();
        services.AddSingleton<FeatureFlagStore>();
        services.AddScoped<IFeatureFlags, TenancyFeatureFlags>();

        services.AddScoped<TenancyRequest>();
        services.AddScoped<ITenantAuditWriter, TenantAuditWriter>();
        services.AddScoped<MembershipGrantValidator>();
        services.AddScoped<Domains.Invitations.Services.TenantInvitationMailer>();
        services.AddSingleton<TenantProvisioner>();
        services.AddTransient<ProvisionTenantJob>();
        services.AddSingleton<TenantMover>();
        services.AddTransient<MoveTenantJob>();
        services.AddTransient<PurgeDeletedTenantsJob>();
        services.AddSingleton<DedicatedTenantDatabaseMigrator>(); // also resolved by the --migrate-only run
        services.AddHostedService(sp => sp.GetRequiredService<DedicatedTenantDatabaseMigrator>());
        Shared.Core.Net.DnsTxtVerifierServiceCollectionExtensions.AddDnsTxtVerifier(services);

        // Billing: subscriber = the tenant in multi-tenant mode, the user otherwise (decided per request from
        // MultiTenancy:Enabled). Registered before Subscriptions, whose defaults use TryAdd.
        services.AddScoped<ISubscriberIdProvider, TenantSubscriberIdProvider>();
        services.AddScoped<ISubscriberInfoProvider, TenantSubscriberInfoProvider>();
        // Notifications addressed to a tenant subscriber reach its owner.
        services.AddScoped<Notification.Contracts.INotificationRecipientResolver, TenantNotificationRecipientResolver>();

        services.AddImportDefinition<MemberImportDefinition>();
        services.AddExportDefinition<MemberExportDefinition>();
        services.AddMediator(TenancyRoot.AssemblyName);
        services.AddCustomMapster(TenancyRoot.AssemblyName);
        services.AddCarterWithAssemblies(TenancyRoot.AssemblyName);

        return services;
    }

    public static IApplicationBuilder UseTenancyModule(this IApplicationBuilder app)
    {
        app.UseMigration<TenancyDbContext>();

        using var scope = app.ApplicationServices.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRecurringJobManager>().AddOrUpdate<PurgeDeletedTenantsJob>(
            PurgeJobId, j => j.ExecuteAsync(JobCancellationToken.Null), Cron.Daily(3));

        return app;
    }
}
