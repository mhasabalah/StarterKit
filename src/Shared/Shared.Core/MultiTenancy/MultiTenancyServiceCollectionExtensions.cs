using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.MultiTenancy.BackgroundJobs;
using Shared.Core.MultiTenancy.Caching;
using Shared.Core.MultiTenancy.Data;
using Shared.Core.MultiTenancy.Web;

namespace Shared.Core.MultiTenancy;

public static class MultiTenancyServiceCollectionExtensions
{
    /// <summary>
    /// Host registration: binds <c>MultiTenancy</c> options and the kernel services. Call once in Program.cs BEFORE
    /// the modules. The Tenancy module replaces <see cref="ITenantStore"/> / <see cref="ITenantMembershipResolver"/>.
    /// </summary>
    public static IServiceCollection AddMultiTenancy(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MultiTenancyOptions>()
            .Bind(configuration.GetSection(MultiTenancyOptions.SectionName))
            .Validate(o => o.MaxBranchDepth is >= 1 and <= 32, "MultiTenancy:MaxBranchDepth must be 1..32.")
            .Validate(o => o.Database.MigrationParallelism >= 1, "MultiTenancy:Database:MigrationParallelism must be >= 1.")
            .Validate(o => o.Database.RlsBypass != RlsBypassMode.Role ||
                           (o.Database.EnableRowLevelSecurity && !string.IsNullOrWhiteSpace(o.Database.HostConnectionString)),
                "MultiTenancy:Database:RlsBypass=Role requires EnableRowLevelSecurity=true and a HostConnectionString.")
            .Validate(o => o.Impersonation.MaxMinutes is >= 5 and <= 60, "MultiTenancy:Impersonation:MaxMinutes must be 5..60.")
            .Validate(o => o.Invitations.AcceptUrl.Contains("{token}", StringComparison.Ordinal),
                "MultiTenancy:Invitations:AcceptUrl must contain the {token} placeholder.")
            .ValidateOnStart();

        services.AddMultiTenancyCore();
        services.AddHostedService<PoolingSafetyCheck>();
        services.AddHostedService<RlsBypassRoleCheck>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in TenantPermissions.Policies)
            {
                options.AddPolicy(TenantPermissions.PolicyName(permission), policy => policy
                    .RequireAuthenticatedUser()
                    .AddRequirements(new TenantPermissionRequirement(permission)));
            }
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, TenantPermissionHandler>());

        // Tenant-aware CORS (tenant subdomains + verified custom domains). Replaces the default provider whether
        // AddCors ran before (Replace) or runs later (its TryAdd then keeps this one).
        services.RemoveAll<ICorsPolicyProvider>();
        services.AddTransient<ICorsPolicyProvider, TenantCorsPolicyProvider>();

        return services;
    }

    /// <summary>
    /// Idempotent defaults (TryAdd) so any DbContext works without <see cref="AddMultiTenancy"/> - tests, tools and
    /// single-tenant hosts get the Default tenant everywhere. Called by <c>AddCustomDbContext</c>.
    /// </summary>
    public static IServiceCollection AddMultiTenancyCore(this IServiceCollection services)
    {
        services.AddOptions<MultiTenancyOptions>();
        services.TryAddSingleton<ITenantContext, TenantContext>();
        services.TryAddSingleton<ITenantStore, DefaultTenantStore>();
        services.TryAddSingleton<ITenantMembershipResolver, NullTenantMembershipResolver>();
        services.TryAddSingleton<ITenantConnectionStringResolver, TenantConnectionStringResolver>();
        services.TryAddSingleton<TenantEntityInterceptor>();
        services.TryAddSingleton<TenantSessionConnectionInterceptor>();
        services.TryAddSingleton<ITenantJobRunner, TenantJobRunner>();
        services.TryAddSingleton<ITenantCacheKeys, TenantCacheKeys>();
        services.AddMemoryCache();
        AddLocalCacheInvalidation(services);

        if (services.All(d => d.ServiceType != typeof(TenantDataContextRegistry)))
        {
            services.AddSingleton(new TenantDataContextRegistry());
        }

        return services;
    }

    /// <summary>
    /// Redis pub/sub bus when an <see cref="StackExchange.Redis.IConnectionMultiplexer"/> is registered (resolved lazily,
    /// so registration order does not matter), otherwise a no-op. The hosted service starts the subscription.
    /// </summary>
    private static void AddLocalCacheInvalidation(IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(ILocalCacheInvalidationBus))) return;

        services.AddSingleton<ILocalCacheInvalidationBus>(sp =>
            sp.GetService<StackExchange.Redis.IConnectionMultiplexer>() is null
                ? new NoOpLocalCacheInvalidationBus()
                : ActivatorUtilities.CreateInstance<RedisLocalCacheInvalidationBus>(sp));
        services.AddHostedService<LocalCacheInvalidationHost>();
    }

    /// <summary>Starts/stops the bus subscription when the bus is the Redis implementation.</summary>
    private sealed class LocalCacheInvalidationHost(ILocalCacheInvalidationBus bus) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            bus is IHostedService hosted ? hosted.StartAsync(cancellationToken) : Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) =>
            bus is IHostedService hosted ? hosted.StopAsync(cancellationToken) : Task.CompletedTask;
    }

    internal static TenantDataContextRegistry GetTenantDataContextRegistry(this IServiceCollection services) =>
        (TenantDataContextRegistry)services
            .First(d => d.ServiceType == typeof(TenantDataContextRegistry))
            .ImplementationInstance!;

    internal static void RegisterTenantDataContext(this IServiceCollection services, Type contextType) =>
        services.GetTenantDataContextRegistry().Add(contextType);
}
