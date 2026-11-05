using System.Collections.Immutable;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;
using IsolationLevel = System.Data.IsolationLevel;

namespace Shared.Core.Data;

public abstract class AppDbContextBase : DbContext, IDbContext
{
    private static readonly MethodInfo TenantFilterMethod = typeof(AppDbContextBase)
        .GetMethod(nameof(ConfigureTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo BranchFilterMethod = typeof(AppDbContextBase)
        .GetMethod(nameof(ConfigureBranchFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private IDbContextTransaction? _currentTransaction;
    private ITenantContext? _tenantContext;

    protected AppDbContextBase(DbContextOptions options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
    }

    // ------------------------------------------------------------------------------------------------------------
    // Multi-tenancy. The filter inputs below are read PER QUERY (EF parameterizes members of the context instance),
    // from the ambient ITenantContext - so a cached model is safe across tenants.
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>The ambient tenant context (DI singleton; a disabled/Default-tenant context outside DI).</summary>
    protected ITenantContext TenantContext => _tenantContext ??= ResolveTenantContext();

    /// <summary>Tenant filter value. <c>null</c> (unresolved tenant) matches NO rows: fail closed.</summary>
    protected Guid? CurrentTenantId => TenantContext.TenantId;

    /// <summary>Only host scope bypasses the tenant filter.</summary>
    protected bool TenantFilterDisabled => TenantContext.IsHostScope;

    protected bool BranchFilterDisabled => TenantContext.IsHostScope || !TenantContext.Branches.IsRestricted;

    protected List<Guid> AllowedBranchIds => TenantContext.Branches.AllowedBranchIds?.ToList() ?? [];

    /// <summary>
    /// Adds the named <c>Tenant</c> filter (+ a <c>tenant_id</c> index) to every <see cref="IMustHaveTenant"/> entity and
    /// the named <c>Branch</c> filter to every <see cref="IBranchScoped"/> entity. Call at the END of
    /// <c>OnModelCreating</c> of every <see cref="ITenantDataContext"/> (after configurations, so indexes exist).
    /// </summary>
    protected void ApplyTenantFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(t => t.BaseType is null && !t.IsOwned())
                     .ToList())
        {
            var clr = entityType.ClrType;

            if (clr.IsAssignableTo(typeof(IMustHaveTenant)))
            {
                TenantFilterMethod.MakeGenericMethod(clr).Invoke(this, [builder]);
                EnsureTenantIndex(entityType);
            }

            if (clr.IsAssignableTo(typeof(IBranchScoped)))
            {
                BranchFilterMethod.MakeGenericMethod(clr).Invoke(this, [builder]);
            }
        }
    }

    private void ConfigureTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, IMustHaveTenant =>
        builder.Entity<TEntity>().HasQueryFilter(QueryFilterNames.Tenant,
            e => TenantFilterDisabled || e.TenantId == CurrentTenantId);

    private void ConfigureBranchFilter<TEntity>(ModelBuilder builder) where TEntity : class, IBranchScoped =>
        builder.Entity<TEntity>().HasQueryFilter(QueryFilterNames.Branch,
            e => BranchFilterDisabled || e.BranchId == null || AllowedBranchIds.Contains(e.BranchId.Value));

    private static void EnsureTenantIndex(IMutableEntityType entityType)
    {
        var tenantId = entityType.FindProperty(nameof(IMustHaveTenant.TenantId));
        if (tenantId is null)
            return;

        var hasLeadingIndex = entityType.GetIndexes().Any(i => i.Properties[0] == tenantId)
                              || entityType.FindPrimaryKey()?.Properties[0] == tenantId;
        if (!hasLeadingIndex)
        {
            entityType.AddIndex(tenantId);
        }
    }

    private ITenantContext ResolveTenantContext()
    {
        var applicationServices = this.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider;

        return applicationServices?.GetService<ITenantContext>()
               ?? new TenantContext(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions()));
    }

    public IExecutionStrategy CreateExecutionStrategy()
    {
        return Database.CreateExecutionStrategy();
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
        {
            return;
        }

        _currentTransaction =
            await Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
            await _currentTransaction?.CommitAsync(cancellationToken)!;
        }
        catch
        {
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            _currentTransaction?.Dispose();
            _currentTransaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_currentTransaction is not null)
                await _currentTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            _currentTransaction?.Dispose();
            _currentTransaction = null;
        }
    }

    //ref: https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency#execution-strategies-and-transactions
    public Task ExecuteTransactionalAsync(CancellationToken cancellationToken = default)
    {
        var strategy = CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await Database.BeginTransactionAsync(IsolationLevel.ReadCommitted,
                    cancellationToken);
            try
            {
                await SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    // NOTE: SaveChangesAsync is deliberately NOT overridden to translate DbUpdateConcurrencyException.
    // EF's exception propagates unchanged so that background code can catch it per item
    // (catch (Exception ex) when (ex.IsConcurrencyConflict())). The ONE translation to the domain
    // ConcurrencyConflictException (HTTP 409) happens in ConcurrencyHandlingBehavior (mediator), and
    // ExceptionStatusMapper maps a raw DbUpdateConcurrencyException to 409 as well.

    public IReadOnlyList<IDomainEvent> GetDomainEvents()
    {
        var domainEntities = ChangeTracker
            .Entries<IAggregate>()
            .Where(x => x.Entity.DomainEvents.Any())
            .Select(x => x.Entity)
            .ToList();

        var domainEvents = domainEntities
            .SelectMany(x => x.DomainEvents)
            .ToImmutableList();

        domainEntities.ForEach(entity => entity.ClearDomainEvents());

        return domainEvents;
    }
}
