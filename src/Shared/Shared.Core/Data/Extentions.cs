using System.Linq.Expressions;
using Humanizer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.Auditing;
using Shared.Core.Data.Interceptors;
using Shared.Core.Data.Seed;
using Shared.Core.Extensions;
using Shared.Core.Messaging.Outbox;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;

namespace Shared.Core.Data;

public static class Extensions
{
    public static IServiceCollection AddCustomDbContext<TContext>(
        this IServiceCollection services, IConfiguration configuration, string? connectionName = "")
        where TContext : DbContext, IDbContext
    {
        services.AddDomainEventDispatch();
        services.TryAddScoped<AuditableEntityInterceptor>();
        services.TryAddScoped<AuditTrailInterceptor>();
        services.Configure<AuditTrailOptions>(configuration.GetSection(AuditTrailOptions.Section));
        services.AddMultiTenancyCore();
        services.AddOutboxCore();

        var isTenantData = typeof(TContext).IsAssignableTo(typeof(ITenantDataContext));
        if (isTenantData)
        {
            services.RegisterTenantDataContext(typeof(TContext));
        }

        services.AddValidateOptions<PostgresOptions>();

        services.AddDbContext<TContext>((sp, options) =>
        {
            var aspireConnectionString =
                configuration.GetConnectionString(connectionName.Kebaberize());
            var connectionString = aspireConnectionString ??
                                   sp.GetRequiredService<PostgresOptions>().ConnectionString;

            ArgumentException.ThrowIfNullOrEmpty(connectionString);

            options.AddInterceptors(
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<DispatchDomainEventsInterceptor>(),
                sp.GetRequiredService<TenantEntityInterceptor>(),
                // AFTER TenantEntityInterceptor (tenant stamped), BEFORE the outbox: audit rows join the same SaveChanges.
                sp.GetRequiredService<AuditTrailInterceptor>(),
                // AFTER TenantEntityInterceptor: copies the (stamped, validated) aggregate tenant onto outbox rows.
                sp.GetRequiredService<OutboxInterceptor>());

            if (isTenantData)
            {
                // DATA PLANE: the shared database or the current tenant's dedicated database (contexts are not pooled,
                // so the connection is chosen per scope), plus the RLS session variables on every connection.
                var tenant = sp.GetRequiredService<ITenantContext>().Tenant;
                connectionString = sp.GetRequiredService<ITenantConnectionStringResolver>()
                    .Resolve(tenant, connectionString);
                options.AddInterceptors(sp.GetRequiredService<TenantSessionConnectionInterceptor>());
            }

            options.UseNpgsql(
                    connectionString,
                    dbOptions =>
                    {
                        dbOptions.MigrationsAssembly(typeof(TContext).Assembly.GetName().Name);
                        // Same history table as the design-time factory (see MigrationsHistory).
                        dbOptions.UseModuleMigrationsHistory<TContext>();
                    })
                .UseSnakeCaseNamingConvention();
            // No legacy timestamp switch: DateTime maps to timestamptz and must be UTC (Kind=Utc) when written.
            // PendingModelChangesWarning is NOT suppressed - a forgotten migration makes Migrate() throw
            // (and Api.E2ETests PendingModelChangesTests fails in CI).
        });

        services.AddScoped<IDbContext>(sp => sp.GetRequiredService<TContext>());

        return services;
    }

    /// <summary>
    /// Registers the domain-event pipeline (idempotent, TryAdd): the scoped <see cref="IDomainEventCollector"/> and
    /// <see cref="DispatchDomainEventsInterceptor"/> (one per DI scope, shared by every DbContext in it), the scoped
    /// <see cref="IDomainEventDispatcher"/>, the singleton <see cref="IUnitOfWorkRunner"/> for background work, and
    /// <see cref="TimeProvider.System"/>. Called by <see cref="AddCustomDbContext{TContext}"/> and
    /// <c>AddMediator</c>; call it yourself only when wiring a DbContext by hand.
    /// </summary>
    public static IServiceCollection AddDomainEventDispatch(this IServiceCollection services)
    {
        services.TryAddScoped<IDomainEventCollector, DomainEventCollector>();
        services.TryAddScoped<DispatchDomainEventsInterceptor>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.TryAddSingleton<IUnitOfWorkRunner, UnitOfWorkRunner>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// Migrates (host scope: RLS bypassed) and seeds the SHARED database of <typeparamref name="TContext"/>, under a
    /// per-database advisory lock so concurrent migrators serialize. Seeders of tenant-data contexts run as the Default
    /// tenant. Skipped when <c>Database:MigrateOnStartup=false</c> (production: the one-shot migrator,
    /// <c>--migrate-only</c>, does it at deploy time). Dedicated tenant databases are migrated and seeded by the
    /// Tenancy module (provisioning + migrator) through <see cref="MigrateAndSeedAsync"/>.
    /// </summary>
    public static IApplicationBuilder UseMigration<TContext>(this IApplicationBuilder app)
        where TContext : DbContext, IDbContext
    {
        var options = DatabaseMigrationOptions.From(app.ApplicationServices.GetRequiredService<IConfiguration>());
        if (!options.ShouldMigrate)
        {
            app.ApplicationServices.GetRequiredService<ILogger<TContext>>()
                .LogInformation("Database:MigrateOnStartup=false - skipping migration and seeding of {Context}",
                    typeof(TContext).Name);
            return app;
        }

        MigrateAndSeedSharedAsync<TContext>(app.ApplicationServices, options).GetAwaiter().GetResult();
        return app;
    }

    private static async Task MigrateAndSeedSharedAsync<TContext>(IServiceProvider services,
        DatabaseMigrationOptions options)
        where TContext : DbContext, IDbContext
    {
        var logger = services.GetRequiredService<ILogger<TContext>>();
        var migrationConnectionString = string.IsNullOrWhiteSpace(options.MigrationConnectionString)
            ? null
            : options.MigrationConnectionString;

        string? lockConnectionString;
        using (TenantScope.BeginHostScope())
        {
            await using var scope = services.CreateAsyncScope();
            lockConnectionString = migrationConnectionString
                                   ?? scope.ServiceProvider.GetRequiredService<TContext>().Database.GetConnectionString();
        }

        await using var migrationLock = await MigrationLock.AcquireAsync(
            lockConnectionString, TimeSpan.FromSeconds(options.LockTimeoutSeconds), logger,
            timeProvider: services.GetService<TimeProvider>());

        using (TenantScope.BeginHostScope())
        {
            await MigrateAsync<TContext>(services, migrationConnectionString);
        }

        using (typeof(TContext).IsAssignableTo(typeof(ITenantDataContext))
                   ? TenantScope.Begin(TenantDefaults.DefaultTenant)
                   : null)
        {
            await SeedDataAsync<TContext>(services);
        }
    }

    /// <summary>Runs the seeders of <paramref name="contextType"/> as <paramref name="tenant"/> (shared-database tenants).</summary>
    public static async Task SeedAsync(IServiceProvider services, Type contextType, TenantInfo tenant)
    {
        using (TenantScope.Begin(tenant))
        {
            await using var scope = services.CreateAsyncScope();
            foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>()
                         .Where(s => s.DbContextType == contextType))
            {
                await seeder.SeedAllAsync();
            }
        }
    }

    /// <summary>
    /// Creates (if needed) and migrates the database of <paramref name="tenant"/> for one tenant-data context -
    /// WITHOUT seeding (e.g. the target of a tenant move, whose rows are copied instead). Serialized per database by
    /// the migration advisory lock.
    /// </summary>
    public static async Task MigrateTenantDatabaseAsync(IServiceProvider services, Type contextType, TenantInfo tenant,
        CancellationToken cancellationToken = default)
    {
        await using var migrationLock = await AcquireTenantLockAsync(services, contextType, tenant, cancellationToken);
        await MigrateTenantDatabaseUnlockedAsync(services, contextType, tenant, cancellationToken);
    }

    /// <summary>Migrates (host scope) and seeds (as the tenant) one tenant-data context in the tenant's database.</summary>
    public static async Task MigrateAndSeedAsync(IServiceProvider services, Type contextType, TenantInfo tenant,
        CancellationToken cancellationToken = default)
    {
        await using var migrationLock = await AcquireTenantLockAsync(services, contextType, tenant, cancellationToken);
        await MigrateTenantDatabaseUnlockedAsync(services, contextType, tenant, cancellationToken);

        using (TenantScope.Begin(tenant))
        {
            await using var scope = services.CreateAsyncScope();
            foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>()
                         .Where(s => s.DbContextType == contextType))
            {
                await seeder.SeedAllAsync();
            }
        }
    }

    private static async Task MigrateTenantDatabaseUnlockedAsync(IServiceProvider services, Type contextType,
        TenantInfo tenant, CancellationToken cancellationToken)
    {
        using (TenantScope.BeginHostScope(tenant))
        {
            await using var scope = services.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            await context.Database.MigrateAsync(cancellationToken);
        }
    }

    private static async Task<MigrationLock> AcquireTenantLockAsync(IServiceProvider services, Type contextType,
        TenantInfo tenant, CancellationToken cancellationToken)
    {
        string? connectionString;
        using (TenantScope.BeginHostScope(tenant))
        {
            await using var scope = services.CreateAsyncScope();
            connectionString = ((DbContext)scope.ServiceProvider.GetRequiredService(contextType))
                .Database.GetConnectionString();
        }

        var options = DatabaseMigrationOptions.From(services.GetRequiredService<IConfiguration>());
        return await MigrationLock.AcquireAsync(connectionString, TimeSpan.FromSeconds(options.LockTimeoutSeconds),
            services.GetRequiredService<ILoggerFactory>().CreateLogger<MigrationLock>(), cancellationToken,
            services.GetService<TimeProvider>());
    }

    // ref: https://github.com/pdevito3/MessageBusTestingInMemHarness/blob/main/RecipeManagement/src/RecipeManagement/Databases/RecipesDbContext.cs
    public static void FilterSoftDeletedProperties(this ModelBuilder modelBuilder)
    {
        Expression<Func<IAggregate, bool>> filterExpr = e => !e.IsDeleted;

        foreach (var mutableEntityType in modelBuilder.Model.GetEntityTypes()
                     .Where(m => m.ClrType.IsAssignableTo(typeof(IEntity))))
        {
            // modify expression to handle correct child type
            var parameter = Expression.Parameter(mutableEntityType.ClrType);

            var body = ReplacingExpressionVisitor
                .Replace(filterExpr.Parameters.First(), parameter, filterExpr.Body);

            var lambdaExpression = Expression.Lambda(body, parameter);

            // NAMED filter: EF Core 10 combines named filters with AND, so the Tenant/Branch filters added by
            // AppDbContextBase.ApplyTenantFilters coexist with it (an unnamed filter would be replaced/conflict).
            mutableEntityType.SetQueryFilter(QueryFilterNames.SoftDelete, lambdaExpression);
        }
    }

    // ref: https://andrewlock.net/customising-asp-net-core-identity-ef-core-naming-conventions-for-postgresql/
    public static void ToSnakeCaseTables(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Replace table names
            entity.SetTableName(entity.GetTableName()?.Underscore());

            var tableObjectIdentifier =
                StoreObjectIdentifier.Table(
                    entity.GetTableName()?.Underscore()!,
                    entity.GetSchema());

            // Replace column names
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(property.GetColumnName(tableObjectIdentifier)?.Underscore());
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(key.GetName()?.Underscore());
            }

            foreach (var key in entity.GetForeignKeys())
            {
                key.SetConstraintName(key.GetConstraintName()?.Underscore());
            }
        }
    }

    private static async Task MigrateAsync<TContext>(IServiceProvider serviceProvider, string? migrationConnectionString)
        where TContext : DbContext, IDbContext
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        if (migrationConnectionString is not null)
        {
            // Privileged role for DDL; the runtime role stays DML-only.
            context.Database.SetConnectionString(migrationConnectionString);
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TContext>>();

        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();

        if (pendingMigrations.Any())
        {
            logger.LogInformation("Applying {Count} pending migrations...",
                pendingMigrations.Count());

            await context.Database.MigrateAsync();
            logger.LogInformation("Migrations applied successfully.");
        }
    }

    private static async Task SeedDataAsync<TContext>(IServiceProvider serviceProvider)
        where TContext : DbContext
    {
        using var scope = serviceProvider.CreateScope();

        var seeders = scope.ServiceProvider.GetServices<IDataSeeder>()
            .Where(seeder => seeder.DbContextType == typeof(TContext));

        foreach (var seeder in seeders)
        {
            await seeder.SeedAllAsync();
        }
    }
}
