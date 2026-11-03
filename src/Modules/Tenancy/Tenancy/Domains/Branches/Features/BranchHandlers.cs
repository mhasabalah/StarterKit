using Microsoft.Extensions.Options;
using Tenancy.Data.Configurations;
using Tenancy.Services;

namespace Tenancy.Domains.Branches.Features;

// All queries run under the named Tenant filter: only the current tenant's branches exist for these handlers.
// Branch-restricted callers additionally only see/touch branches inside their scope.

public class GetBranchesHandler(TenancyDbContext dbContext, TenancyRequest request)
    : IQueryHandler<GetBranchesQuery, IReadOnlyList<BranchTreeNodeDto>>
{
    public async Task<IReadOnlyList<BranchTreeNodeDto>> Handle(GetBranchesQuery query, CancellationToken cancellationToken)
    {
        var branches = await dbContext.Branches.AsNoTracking()
            .Where(b => query.IncludeInactive || b.IsActive)
            .OrderBy(b => b.Depth).ThenBy(b => b.Name)
            .Select(b => new { b.Id, b.ParentId, b.Code, b.Name, b.IsHeadOffice, b.IsActive })
            .ToListAsync(cancellationToken);

        var scope = request.Branches;
        var visible = branches.Where(b => scope.Allows(b.Id)).ToList();
        var nodes = visible.ToDictionary(b => b.Id,
            b => new BranchTreeNodeDto(b.Id, b.ParentId, b.Code, b.Name, b.IsHeadOffice, b.IsActive, []));

        var roots = new List<BranchTreeNodeDto>();
        foreach (var branch in visible)
        {
            if (branch.ParentId is { } parentId && nodes.TryGetValue(parentId, out var parent))
                parent.Children.Add(nodes[branch.Id]);
            else
                roots.Add(nodes[branch.Id]); // real root, or the top of a restricted user's subtree
        }

        return roots;
    }
}

public class GetBranchByIdHandler(TenancyDbContext dbContext, TenancyRequest request)
    : IQueryHandler<GetBranchByIdQuery, BranchDto>
{
    public async Task<BranchDto> Handle(GetBranchByIdQuery query, CancellationToken cancellationToken)
    {
        if (!request.Branches.Allows(query.Id)) throw new NotFoundException(nameof(Branch), query.Id);

        return await dbContext.Branches.AsNoTracking()
                   .Where(b => b.Id == query.Id)
                   .Select(b => new BranchDto(b.Id, b.ParentId, b.Code, b.Name, b.Depth, b.IsHeadOffice, b.IsActive,
                       b.Address, b.Phone, b.TimeZone, b.Version))
                   .FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException(nameof(Branch), query.Id);
    }
}

public class CreateBranchCommandValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Branch.CodeMaxLength)
            .Matches("^[A-Za-z0-9_-]+$").WithMessage("Code may contain letters, digits, '-' and '_' only.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Branch.NameMaxLength);
        RuleFor(x => x.Address).MaximumLength(Branch.AddressMaxLength);
        RuleFor(x => x.ParentId).NotEqual(Guid.Empty).When(x => x.ParentId.HasValue);
    }
}

public class CreateBranchHandler(
    TenancyDbContext dbContext,
    TenancyRequest request,
    ITenantAuditWriter audit,
    ITenantMembershipResolver memberships,
    IOptions<MultiTenancyOptions> options) : ICommandHandler<CreateBranchCommand, CreateBranchResult>
{
    public async Task<CreateBranchResult> Handle(CreateBranchCommand command, CancellationToken cancellationToken)
    {
        Branch? parent = null;
        if (command.ParentId is { } parentId)
        {
            request.EnsureBranchesInScope([parentId]);
            parent = await dbContext.Branches.FirstOrDefaultAsync(b => b.Id == parentId, cancellationToken)
                     ?? throw new NotFoundException(nameof(Branch), parentId);
        }
        else if (request.Branches.IsRestricted)
        {
            throw new TenantAccessDeniedException(TenantErrors.BranchNotAllowed,
                "branch-restricted members can only create branches under their own branches.");
        }

        var branch = Branch.Create(command.Code, command.Name, parent, options.Value.MaxBranchDepth,
            address: command.Address, phone: command.Phone, timeZone: command.TimeZone);
        branch.TenantId = request.TenantId;
        dbContext.Branches.Add(branch);
        audit.Record(TenantAuditActions.BranchCreated, nameof(Branch), branch.Id.ToString(),
            new { branch.Code, branch.Name, command.ParentId });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(BranchConfiguration.CodeIndexName))
        {
            throw new BranchCodeAlreadyExistsException(branch.Code);
        }

        // Members assigned to the parent "with descendants" now also cover this branch.
        if (parent is not null) await memberships.InvalidateAsync(request.TenantId, null, cancellationToken);

        return new CreateBranchResult(branch.Id);
    }
}

public class UpdateBranchCommandValidator : AbstractValidator<UpdateBranchCommand>
{
    public UpdateBranchCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Branch.NameMaxLength);
        RuleFor(x => x.Address).MaximumLength(Branch.AddressMaxLength);
    }
}

public class UpdateBranchHandler(TenancyDbContext dbContext, TenancyRequest request, ITenantAuditWriter audit)
    : ICommandHandler<UpdateBranchCommand, UpdateBranchResult>
{
    public async Task<UpdateBranchResult> Handle(UpdateBranchCommand command, CancellationToken cancellationToken)
    {
        request.EnsureBranchesInScope([command.Id]);
        var branch = await dbContext.Branches.FirstOrDefaultAsync(b => b.Id == command.Id, cancellationToken)
                     ?? throw new NotFoundException(nameof(Branch), command.Id);
        dbContext.ExpectVersion(branch, command.Version);

        branch.UpdateDetails(command.Name, command.Address, command.Phone, command.TimeZone);
        audit.Record(TenantAuditActions.BranchUpdated, nameof(Branch), branch.Id.ToString(), command);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new UpdateBranchResult(branch.Id, branch.Version);
    }
}

public class MoveBranchCommandValidator : AbstractValidator<MoveBranchCommand>
{
    public MoveBranchCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.NewParentId).NotEqual(Guid.Empty).When(x => x.NewParentId.HasValue);
    }
}

/// <summary>
/// Re-parents a branch and rewrites the materialized path/depth of its whole subtree in ONE transaction
/// (set-based UPDATE, no per-row round trips). Membership caches of the tenant are dropped afterwards because
/// "branch + descendants" scopes may have changed.
/// </summary>
public class MoveBranchHandler(
    TenancyDbContext dbContext,
    TenancyRequest request,
    ITenantAuditWriter audit,
    ITenantMembershipResolver memberships,
    IOptions<MultiTenancyOptions> options) : ICommandHandler<MoveBranchCommand, MoveBranchResult>
{
    public async Task<MoveBranchResult> Handle(MoveBranchCommand command, CancellationToken cancellationToken)
    {
        request.EnsureBranchesInScope(command.NewParentId is { } p ? [command.Id, p] : [command.Id]);
        if (command.NewParentId is null && request.Branches.IsRestricted)
            throw new TenantAccessDeniedException(TenantErrors.BranchNotAllowed, "only unrestricted members can create roots.");

        var moved = 0;
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            var branch = await dbContext.Branches.FirstOrDefaultAsync(b => b.Id == command.Id, cancellationToken)
                         ?? throw new NotFoundException(nameof(Branch), command.Id);
            dbContext.ExpectVersion(branch, command.Version);

            Branch? parent = null;
            if (command.NewParentId is { } parentId)
            {
                parent = await dbContext.Branches.FirstOrDefaultAsync(b => b.Id == parentId, cancellationToken)
                         ?? throw new NotFoundException(nameof(Branch), parentId);
            }

            var oldPath = branch.Path;
            var subtreeHeight = await dbContext.Branches
                .Where(b => b.Path.StartsWith(oldPath))
                .MaxAsync(b => b.Depth, cancellationToken) - branch.Depth;

            var (oldPrefix, newPrefix, depthDelta) = branch.MoveUnder(parent, options.Value.MaxBranchDepth, subtreeHeight);
            audit.Record(TenantAuditActions.BranchMoved, nameof(Branch), branch.Id.ToString(),
                new { command.NewParentId, From = oldPrefix, To = newPrefix });
            await dbContext.SaveChangesAsync(cancellationToken);

            if (oldPrefix != newPrefix)
            {
                var id = branch.Id;
                var prefixLength = oldPrefix.Length;
                moved = await dbContext.Branches
                    .Where(b => b.Id != id && b.Path.StartsWith(oldPrefix))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.Path, b => newPrefix + b.Path.Substring(prefixLength))
                        .SetProperty(b => b.Depth, b => b.Depth + depthDelta), cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        });

        await memberships.InvalidateAsync(request.TenantId, null, cancellationToken);
        return new MoveBranchResult(command.Id, moved);
    }
}

public class SetBranchActiveHandler(TenancyDbContext dbContext, TenancyRequest request, ITenantAuditWriter audit)
    : ICommandHandler<SetBranchActiveCommand, SetBranchActiveResult>
{
    public async Task<SetBranchActiveResult> Handle(SetBranchActiveCommand command, CancellationToken cancellationToken)
    {
        request.EnsureBranchesInScope([command.Id]);
        var branch = await dbContext.Branches.FirstOrDefaultAsync(b => b.Id == command.Id, cancellationToken)
                     ?? throw new NotFoundException(nameof(Branch), command.Id);

        if (command.IsActive)
        {
            branch.Activate();
        }
        else
        {
            var path = branch.Path;
            if (await dbContext.Branches.AnyAsync(b => b.Id != branch.Id && b.IsActive && b.Path.StartsWith(path),
                    cancellationToken))
                throw new InvalidBranchException("Deactivate the child branches first.");
            branch.Deactivate();
        }

        audit.Record(command.IsActive ? TenantAuditActions.BranchActivated : TenantAuditActions.BranchDeactivated,
            nameof(Branch), branch.Id.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SetBranchActiveResult(branch.Id, branch.IsActive);
    }
}
