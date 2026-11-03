using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Tenancy.Services;

namespace Tenancy.Domains.Tenants.Features;

// Platform (host) administration of tenants. Endpoints require the platform permissions tenants.read /
// tenants.manage (+ admin 2FA rules of the perm:* policies). Every action is recorded in the tenant's own audit
// trail, so the tenant can see what the platform did.

public class GetHostTenantsHandler(TenancyDbContext dbContext)
    : IQueryHandler<GetHostTenantsQuery, PaginatedResult<HostTenantDto>>
{
    public async Task<PaginatedResult<HostTenantDto>> Handle(GetHostTenantsQuery query, CancellationToken cancellationToken)
    {
        using (TenantScope.BeginHostScope())
        {
            var tenants = dbContext.Tenants.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim().ToLowerInvariant();
                tenants = tenants.Where(t => t.Identifier.Contains(term) || t.Name.ToLower().Contains(term));
            }

            if (query.Status is { } status) tenants = tenants.Where(t => t.Status == status);

            return await tenants
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new HostTenantDto(t.Id, t.Identifier, t.Name, t.Status, t.Isolation, t.Region, t.OwnerUserId,
                    dbContext.Memberships.Count(m => m.TenantId == t.Id && m.Status == TenantMembershipStatus.Active),
                    t.CreatedAt ?? DateTime.MinValue, t.DeletionRequestedAt, t.PurgeAfter))
                .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
        }
    }
}

public class SuspendTenantCommandValidator : AbstractValidator<SuspendTenantCommand>
{
    public SuspendTenantCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(Tenant.ReasonMaxLength);
    }
}

public class SuspendTenantHandler(TenancyDbContext dbContext, ITenantAuditWriter audit)
    : ICommandHandler<SuspendTenantCommand, TenantStatusResult>
{
    public Task<TenantStatusResult> Handle(SuspendTenantCommand command, CancellationToken cancellationToken) =>
        HostTenantAction.RunAsync(dbContext, audit, command.TenantId, TenantAuditActions.TenantSuspended,
            t => t.Suspend(command.Reason), new { command.Reason }, cancellationToken);
}

public class ReactivateTenantHandler(TenancyDbContext dbContext, ITenantAuditWriter audit)
    : ICommandHandler<ReactivateTenantCommand, TenantStatusResult>
{
    public Task<TenantStatusResult> Handle(ReactivateTenantCommand command, CancellationToken cancellationToken) =>
        HostTenantAction.RunAsync(dbContext, audit, command.TenantId, TenantAuditActions.TenantReactivated,
            t => t.Reactivate(), null, cancellationToken);
}

public class DeleteTenantHandler(
    TenancyDbContext dbContext,
    ITenantAuditWriter audit,
    IOptions<MultiTenancyOptions> options,
    TimeProvider timeProvider) : ICommandHandler<DeleteTenantCommand, TenantStatusResult>
{
    public Task<TenantStatusResult> Handle(DeleteTenantCommand command, CancellationToken cancellationToken) =>
        HostTenantAction.RunAsync(dbContext, audit, command.TenantId, TenantAuditActions.TenantDeletionRequested,
            t => t.RequestDeletion(timeProvider.GetUtcNow().UtcDateTime, options.Value.DeletionGracePeriodDays,
                command.Reason),
            new { command.Reason, ByPlatform = true }, cancellationToken);
}

public class CancelTenantDeletionHandler(TenancyDbContext dbContext, ITenantAuditWriter audit)
    : ICommandHandler<CancelTenantDeletionCommand, TenantStatusResult>
{
    public Task<TenantStatusResult> Handle(CancelTenantDeletionCommand command, CancellationToken cancellationToken) =>
        HostTenantAction.RunAsync(dbContext, audit, command.TenantId, TenantAuditActions.TenantDeletionCancelled,
            t => t.CancelDeletion(), null, cancellationToken);
}

public class ChangeTenantIsolationHandler(TenancyDbContext dbContext, ITenantAuditWriter audit)
    : ICommandHandler<ChangeTenantIsolationCommand, TenantStatusResult>
{
    public Task<TenantStatusResult> Handle(ChangeTenantIsolationCommand command, CancellationToken cancellationToken) =>
        HostTenantAction.RunAsync(dbContext, audit, command.TenantId, TenantAuditActions.TenantIsolationChanged,
            t =>
            {
                if (t.Status != TenantStatus.Suspended)
                    throw new InvalidTenantException(
                        "Suspend the tenant and copy its data first (see wiki/MultiTenancy/Operations.md).");
                t.ChangeIsolation(command.Isolation, command.ConnectionSecretRef);
            },
            new { Isolation = command.Isolation.ToString(), command.ConnectionSecretRef }, cancellationToken);
}

public class MoveTenantCommandValidator : AbstractValidator<MoveTenantCommand>
{
    public MoveTenantCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Isolation).IsInEnum();
        RuleFor(x => x.ConnectionSecretRef).MaximumLength(128);
    }
}

/// <summary>
/// Validates the target (resolvable connection, not the live database), marks the tenant <c>Migrating</c> and queues
/// <see cref="MoveTenantJob"/>.
/// </summary>
public class MoveTenantHandler(
    TenancyDbContext dbContext,
    ITenantAuditWriter audit,
    IBackgroundJobClient jobs,
    ITenantConnectionStringResolver connections,
    TenantDataContextRegistry registry,
    IOptions<MultiTenancyOptions> options,
    IConfiguration configuration) : ICommandHandler<MoveTenantCommand, TenantStatusResult>
{
    public async Task<TenantStatusResult> Handle(MoveTenantCommand command, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
            throw new InvalidTenantException("Tenant moves need multi-tenancy (MultiTenancy:Enabled).");
        if (registry.ContextTypes.Count == 0)
            throw new InvalidTenantException("There is no tenant data to move.");

        var shared = configuration.GetConnectionString("starterkit")
                     ?? throw new InvalidOperationException("ConnectionStrings:starterkit is not configured.");

        var result = await HostTenantAction.RunAsync(dbContext, audit, command.TenantId,
            TenantAuditActions.TenantMoveRequested,
            t =>
            {
                var source = connections.Resolve(t.ToInfo(), shared);
                t.BeginMove(command.Isolation, command.ConnectionSecretRef);

                string target;
                try
                {
                    target = connections.Resolve(t.ToMoveTargetInfo(), shared);
                }
                catch (Exception ex) when (ex is not InvalidTenantException)
                {
                    throw new InvalidTenantException($"The target database cannot be resolved: {ex.Message}");
                }

                if (TenantDataMover.SameDatabase(source, target))
                    throw new InvalidTenantException("The target is the database the tenant already lives in.");
            },
            new { Isolation = command.Isolation.ToString(), command.ConnectionSecretRef }, cancellationToken);

        jobs.Enqueue<MoveTenantJob>(j => j.ExecuteAsync(command.TenantId, JobCancellationToken.Null));
        return result;
    }
}

public class RetryTenantMoveHandler(TenancyDbContext dbContext, IBackgroundJobClient jobs)
    : ICommandHandler<RetryTenantMoveCommand, TenantStatusResult>
{
    public async Task<TenantStatusResult> Handle(RetryTenantMoveCommand command, CancellationToken cancellationToken)
    {
        using (TenantScope.BeginHostScope())
        {
            var tenant = await dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == command.TenantId, cancellationToken)
                         ?? throw new NotFoundException(nameof(Tenant), command.TenantId);
            if (tenant.Status != TenantStatus.Migrating)
                throw new InvalidTenantException("The tenant is not being moved.");

            jobs.Enqueue<MoveTenantJob>(j => j.ExecuteAsync(tenant.Id, JobCancellationToken.Null));
            return new TenantStatusResult(tenant.Id, tenant.Status);
        }
    }
}

public class AbortTenantMoveHandler(TenancyDbContext dbContext, ITenantAuditWriter audit)
    : ICommandHandler<AbortTenantMoveCommand, TenantStatusResult>
{
    public Task<TenantStatusResult> Handle(AbortTenantMoveCommand command, CancellationToken cancellationToken) =>
        HostTenantAction.RunAsync(dbContext, audit, command.TenantId, TenantAuditActions.TenantMoveFailed,
            t => t.FailMove(), new { Error = "Aborted by a platform administrator." }, cancellationToken);
}

public class RetryTenantProvisioningHandler(TenancyDbContext dbContext, IBackgroundJobClient jobs)
    : ICommandHandler<RetryTenantProvisioningCommand, TenantStatusResult>
{
    public async Task<TenantStatusResult> Handle(RetryTenantProvisioningCommand command, CancellationToken cancellationToken)
    {
        using (TenantScope.BeginHostScope())
        {
            var tenant = await dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == command.TenantId, cancellationToken)
                         ?? throw new NotFoundException(nameof(Tenant), command.TenantId);
            if (tenant.Status is not (TenantStatus.ProvisioningFailed or TenantStatus.Provisioning or TenantStatus.Active))
                throw new InvalidTenantException($"A {tenant.Status} tenant cannot be provisioned.");

            jobs.Enqueue<ProvisionTenantJob>(j => j.ExecuteAsync(tenant.Id, JobCancellationToken.Null));
            return new TenantStatusResult(tenant.Id, tenant.Status);
        }
    }
}

internal static class HostTenantAction
{
    public static async Task<TenantStatusResult> RunAsync(TenancyDbContext db, ITenantAuditWriter audit, Guid tenantId,
        string action, Action<Tenant> change, object? data, CancellationToken ct)
    {
        using (TenantScope.BeginHostScope())
        {
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
                         ?? throw new NotFoundException(nameof(Tenant), tenantId);
            change(tenant);
            audit.Record(action, nameof(Tenant), tenant.Id.ToString(), data, tenant.Id);
            await db.SaveChangesAsync(ct);
            return new TenantStatusResult(tenant.Id, tenant.Status);
        }
    }
}
