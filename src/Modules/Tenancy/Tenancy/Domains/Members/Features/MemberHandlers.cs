using UserDirectory.Contracts.Queries;
using Microsoft.Extensions.Options;
using Subscriptions.Contracts.Subscriptions.Queries;
using Tenancy.Domains.Invitations.Services;
using Tenancy.Services;

namespace Tenancy.Domains.Members.Features;

// Tenant-filtered: these handlers only ever see memberships/roles/branches of the current tenant.

public class GetMembersHandler(TenancyDbContext dbContext, ISender sender)
    : IQueryHandler<GetMembersQuery, PaginatedResult<MemberDto>>
{
    public async Task<PaginatedResult<MemberDto>> Handle(GetMembersQuery query, CancellationToken cancellationToken)
    {
        var memberships = dbContext.Memberships.AsNoTracking();
        if (query.Status is { } status)
            memberships = memberships.Where(m => m.Status == status);
        if (query.RoleId is { } roleId)
            memberships = memberships.Where(m => m.RoleIds.Contains(roleId));

        var page = await memberships
            .OrderByDescending(m => m.IsOwner).ThenBy(m => m.JoinedAt).ThenBy(m => m.Id)
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);

        var rows = page.Result ?? [];
        if (query.BranchId is { } branchId)
            rows = rows.Where(m => m.Branches.Any(b => b.BranchId == branchId)).ToList();

        var roles = await dbContext.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        var users = rows.Count == 0
            ? []
            : (await sender.Send(new GetUserSummariesQuery(rows.Select(m => m.UserId).ToList()), cancellationToken))
            .ToDictionary(u => u.UserId);

        return new PaginatedResult<MemberDto>
        {
            PageInfoResult = page.PageInfoResult,
            Result = rows.Select(m => new MemberDto(
                    m.UserId,
                    users.TryGetValue(m.UserId, out var u) ? u.DisplayName : null,
                    users.TryGetValue(m.UserId, out var e) ? e.Email : null,
                    m.Status,
                    m.IsOwner,
                    m.RoleIds,
                    m.RoleIds.Where(roles.ContainsKey).Select(id => roles[id]).ToList(),
                    m.Branches,
                    m.JoinedAt,
                    m.Version))
                .ToList()
        };
    }
}

public class GetMyMembershipHandler(TenancyDbContext dbContext, TenancyRequest request)
    : IQueryHandler<GetMyMembershipQuery, MyMembershipDto>
{
    public async Task<MyMembershipDto> Handle(GetMyMembershipQuery query, CancellationToken cancellationToken)
    {
        var userId = request.UserId;
        var branches = await dbContext.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.Branches)
            .FirstOrDefaultAsync(cancellationToken) ?? [];

        return new MyMembershipDto(
            request.TenantId,
            userId,
            request.IsOwner,
            request.Permissions.Order(StringComparer.Ordinal).ToList(),
            request.Branches.AllowedBranchIds,
            branches);
    }
}

public class UpdateMemberCommandValidator : AbstractValidator<UpdateMemberCommand>
{
    public UpdateMemberCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.RoleIds).NotNull();
        RuleFor(x => x.Branches).NotNull();
        RuleForEach(x => x.Branches).Must(b => b.BranchId != Guid.Empty).WithMessage("Branch id is required.");
    }
}

public class UpdateMemberHandler(
    TenancyDbContext dbContext,
    TenancyRequest request,
    MembershipGrantValidator grants,
    ITenantAuditWriter audit) : ICommandHandler<UpdateMemberCommand, UpdateMemberResult>
{
    public async Task<UpdateMemberResult> Handle(UpdateMemberCommand command, CancellationToken cancellationToken)
    {
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(m => m.UserId == command.UserId, cancellationToken)
                         ?? throw new NotFoundException("Member", command.UserId);
        dbContext.ExpectVersion(membership, command.Version);

        if (membership.UserId == request.UserId && !request.IsOwner)
            throw new ForbiddenException("You cannot change your own roles or branches.");
        if (membership.IsOwner && !request.IsOwner)
            throw new ForbiddenException("Only the owner can change the owner's membership.");

        await grants.EnsureValidAsync(command.RoleIds, command.Branches, cancellationToken);

        membership.SetRoles(command.RoleIds);
        membership.SetBranches(command.Branches);
        audit.Record(TenantAuditActions.MemberUpdated, "Member", command.UserId.ToString(),
            new { command.RoleIds, command.Branches });

        await dbContext.SaveChangesAsync(cancellationToken);
        return new UpdateMemberResult(command.UserId);
    }
}

public class SetMemberStatusHandler(TenancyDbContext dbContext, TenancyRequest request, ITenantAuditWriter audit)
    : ICommandHandler<SetMemberStatusCommand, SetMemberStatusResult>
{
    public async Task<SetMemberStatusResult> Handle(SetMemberStatusCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == request.UserId)
            throw new ForbiddenException("You cannot change your own status.");

        var membership = await dbContext.Memberships.FirstOrDefaultAsync(m => m.UserId == command.UserId, cancellationToken)
                         ?? throw new NotFoundException("Member", command.UserId);

        if (command.Active)
            membership.Activate();
        else
            membership.Suspend();

        audit.Record(command.Active ? TenantAuditActions.MemberReactivated : TenantAuditActions.MemberSuspended,
            "Member", command.UserId.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);
        return new SetMemberStatusResult(command.UserId, membership.Status);
    }
}

public class RemoveMemberHandler(TenancyDbContext dbContext, TenancyRequest request, ITenantAuditWriter audit)
    : ICommandHandler<RemoveMemberCommand, RemoveMemberResult>
{
    public async Task<RemoveMemberResult> Handle(RemoveMemberCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == request.UserId)
            throw new ForbiddenException("Use 'leave tenant' to remove yourself.");

        var membership = await dbContext.Memberships.FirstOrDefaultAsync(m => m.UserId == command.UserId, cancellationToken)
                         ?? throw new NotFoundException("Member", command.UserId);
        membership.Remove();
        dbContext.Memberships.Remove(membership);
        audit.Record(TenantAuditActions.MemberRemoved, "Member", command.UserId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);
        return new RemoveMemberResult(command.UserId);
    }
}

public class LeaveTenantHandler(TenancyDbContext dbContext, TenancyRequest request, ITenantAuditWriter audit)
    : ICommandHandler<LeaveTenantCommand, LeaveTenantResult>
{
    public async Task<LeaveTenantResult> Handle(LeaveTenantCommand command, CancellationToken cancellationToken)
    {
        var userId = request.UserId;
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken)
                         ?? throw new NotFoundException("Member", userId);
        membership.Remove();
        dbContext.Memberships.Remove(membership);
        audit.Record(TenantAuditActions.MemberLeft, "Member", userId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);
        return new LeaveTenantResult(request.TenantId);
    }
}

// ============================================================================================================
// Invitations
// ============================================================================================================

public class GetInvitationsHandler(TenancyDbContext dbContext, TimeProvider timeProvider)
    : IQueryHandler<GetInvitationsQuery, IReadOnlyList<InvitationDto>>
{
    public async Task<IReadOnlyList<InvitationDto>> Handle(GetInvitationsQuery query, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var invitations = dbContext.Invitations.AsNoTracking();
        invitations = query.Status switch
        {
            TenantInvitationStatus.Pending => invitations.Where(i => i.Status == TenantInvitationStatus.Pending && i.ExpiresAt > now),
            TenantInvitationStatus.Expired => invitations.Where(i => i.Status == TenantInvitationStatus.Expired ||
                                                                     (i.Status == TenantInvitationStatus.Pending && i.ExpiresAt <= now)),
            { } status => invitations.Where(i => i.Status == status),
            null => invitations
        };

        var rows = await invitations.OrderByDescending(i => i.CreatedAt).Take(500).ToListAsync(cancellationToken);
        return rows.Select(i => new InvitationDto(i.Id, i.Email, i.RoleIds, i.Branches,
                i.Status == TenantInvitationStatus.Pending && i.ExpiresAt <= now ? TenantInvitationStatus.Expired : i.Status,
                i.ExpiresAt, i.InvitedBy, i.CreatedAt))
            .ToList();
    }
}

public class InviteMemberCommandValidator : AbstractValidator<InviteMemberCommand>
{
    public InviteMemberCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(TenantInvitation.EmailMaxLength);
        RuleFor(x => x.RoleIds).NotNull();
        RuleFor(x => x.Branches).NotNull();
        RuleFor(x => x.ExpiresInDays).InclusiveBetween(1, 30).When(x => x.ExpiresInDays.HasValue);
    }
}

/// <summary>
/// Invites an e-mail. Enforces the plan's <c>MaxUsers</c> (active members + pending invitations) when the tenant has
/// an active subscription, and the anti-escalation rules for the granted roles/branches.
/// </summary>
public class InviteMemberHandler(
    TenancyDbContext dbContext,
    TenancyRequest request,
    MembershipGrantValidator grants,
    ITenantAuditWriter audit,
    ISender sender,
    TenantInvitationMailer mailer,
    ITenantContext tenantContext,
    IOptions<MultiTenancyOptions> options,
    TimeProvider timeProvider,
    ILogger<InviteMemberHandler> logger) : ICommandHandler<InviteMemberCommand, InviteMemberResult>
{
    public async Task<InviteMemberResult> Handle(InviteMemberCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var email = TenantInvitation.NormalizeEmail(command.Email);

        await grants.EnsureValidAsync(command.RoleIds, command.Branches, cancellationToken);

        if (await dbContext.Invitations.AnyAsync(i => i.Email == email && i.Status == TenantInvitationStatus.Pending &&
                                                      i.ExpiresAt > now, cancellationToken))
            throw new ConflictException($"'{email}' already has a pending invitation.");

        await EnsureSeatAvailableAsync(now, cancellationToken);

        var (invitation, token) = TenantInvitation.Create(request.TenantId, email, command.RoleIds, command.Branches,
            request.UserId, now, TimeSpan.FromDays(command.ExpiresInDays ?? 7));
        dbContext.Invitations.Add(invitation);
        audit.Record(TenantAuditActions.MemberInvited, nameof(TenantInvitation), invitation.Id.ToString(),
            new { email, command.RoleIds, command.Branches });

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Invitation {InvitationId} created for tenant {TenantId}", invitation.Id, request.TenantId);

        var queued = await mailer.SendAsync(invitation, token, tenantContext.Tenant!, request.UserId, now,
            cancellationToken);
        return new InviteMemberResult(invitation.Id,
            options.Value.Invitations.ReturnTokenInResponse ? token : null, invitation.ExpiresAt, queued);
    }

    private async Task EnsureSeatAvailableAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (!request.MultiTenancyEnabled)
            return;

        int? maxUsers;
        try
        {
            maxUsers = (await sender.Send(new GetActivePlanLimitsQuery(request.TenantId), cancellationToken))?.MaxUsers;
        }
        catch (NotFoundException)
        {
            maxUsers = null; // no subscription → no plan limit (configure a free plan to cap seats)
        }

        if (maxUsers is not { } max)
            return;

        var seats = await dbContext.Memberships.CountAsync(m => m.Status == TenantMembershipStatus.Active, cancellationToken)
                    + await dbContext.Invitations.CountAsync(i => i.Status == TenantInvitationStatus.Pending && i.ExpiresAt > now,
                        cancellationToken);
        if (seats >= max)
            throw new TenantLimitExceededException($"Your plan allows {max} users. Upgrade the plan to invite more.");
    }
}

public class ResendInvitationCommandValidator : AbstractValidator<ResendInvitationCommand>
{
    public ResendInvitationCommandValidator()
    {
        RuleFor(x => x.InvitationId).NotEmpty();
        RuleFor(x => x.ExpiresInDays).InclusiveBetween(1, 30).When(x => x.ExpiresInDays.HasValue);
    }
}

/// <summary>New token + lifetime for a pending/expired invitation, then the e-mail again (the old link dies).</summary>
public class ResendInvitationHandler(
    TenancyDbContext dbContext,
    TenancyRequest request,
    ITenantAuditWriter audit,
    TenantInvitationMailer mailer,
    ITenantContext tenantContext,
    IOptions<MultiTenancyOptions> options,
    TimeProvider timeProvider) : ICommandHandler<ResendInvitationCommand, InviteMemberResult>
{
    public async Task<InviteMemberResult> Handle(ResendInvitationCommand command, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var invitation = await dbContext.Invitations.FirstOrDefaultAsync(i => i.Id == command.InvitationId, cancellationToken)
                         ?? throw new NotFoundException(nameof(TenantInvitation), command.InvitationId);

        var token = invitation.Renew(now, TimeSpan.FromDays(command.ExpiresInDays ?? 7));
        audit.Record(TenantAuditActions.InvitationResent, nameof(TenantInvitation), invitation.Id.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);

        var queued = await mailer.SendAsync(invitation, token, tenantContext.Tenant!, request.UserId, now,
            cancellationToken);
        return new InviteMemberResult(invitation.Id,
            options.Value.Invitations.ReturnTokenInResponse ? token : null, invitation.ExpiresAt, queued);
    }
}

public class RevokeInvitationHandler(TenancyDbContext dbContext, ITenantAuditWriter audit)
    : ICommandHandler<RevokeInvitationCommand, RevokeInvitationResult>
{
    public async Task<RevokeInvitationResult> Handle(RevokeInvitationCommand command, CancellationToken cancellationToken)
    {
        var invitation = await dbContext.Invitations.FirstOrDefaultAsync(i => i.Id == command.InvitationId, cancellationToken)
                         ?? throw new NotFoundException(nameof(TenantInvitation), command.InvitationId);
        invitation.Revoke();
        audit.Record(TenantAuditActions.InvitationRevoked, nameof(TenantInvitation), invitation.Id.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);
        return new RevokeInvitationResult(invitation.Id);
    }
}

public class AcceptInvitationCommandValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationCommandValidator() => RuleFor(x => x.Token).NotEmpty().MaximumLength(200);
}

/// <summary>
/// The signed-in user joins the inviting tenant. The invitation is found by token hash across tenants (host scope);
/// the membership is then created INSIDE that tenant's scope. The user's e-mail must match and be verified.
/// </summary>
public class AcceptInvitationHandler(
    TenancyDbContext dbContext,
    IHttpContextService httpContextService,
    ITenantAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<AcceptInvitationCommand, AcceptInvitationResult>
{
    public async Task<AcceptInvitationResult> Handle(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserIdAsGuid() ?? throw new UnauthorizedException("Authentication is required.");
        if (!bool.TryParse(httpContextService.GetClaimValue("email_verified"), out var verified) || !verified)
            throw new ForbiddenException("Verify your e-mail address before accepting invitations.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var hash = TenantInvitation.HashToken(command.Token);

        TenantInvitation invitation;
        Tenant tenant;
        using (TenantScope.BeginHostScope())
        {
            invitation = await dbContext.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken)
                         ?? throw new NotFoundException("This invitation does not exist.");
            tenant = await dbContext.Tenants.AsNoTracking().FirstAsync(t => t.Id == invitation.TenantId, cancellationToken);
        }

        if (tenant.Status != TenantStatus.Active)
            throw new TenantAccessDeniedException(TenantErrors.TenantUnavailable, "this tenant is not accepting members.");

        using (TenantScope.Begin(tenant.ToInfo()))
        {
            if (await dbContext.Memberships.AnyAsync(m => m.UserId == userId, cancellationToken))
                throw new ConflictException("You are already a member of this tenant.");

            invitation.Accept(userId, httpContextService.GetEmail(), now);

            // Roles/branches deleted since the invitation are silently dropped.
            var roleIds = await dbContext.Roles.Where(r => invitation.RoleIds.Contains(r.Id)).Select(r => r.Id)
                .ToListAsync(cancellationToken);
            var branchIds = invitation.Branches.Select(b => b.BranchId).ToList();
            var existingBranches = await dbContext.Branches.Where(b => branchIds.Contains(b.Id)).Select(b => b.Id)
                .ToListAsync(cancellationToken);
            if (roleIds.Count == 0)
                roleIds.Add(SystemTenantRoles.IdOf(tenant.Id, SystemTenantRoles.Member));

            dbContext.Memberships.Add(TenantMembership.Create(tenant.Id, userId, roleIds,
                invitation.Branches.Where(b => existingBranches.Contains(b.BranchId)), now, invitedBy: invitation.InvitedBy));
            audit.Record(TenantAuditActions.MemberJoined, "Member", userId.ToString(), new { invitation.Id }, tenant.Id);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AcceptInvitationResult(tenant.Id, tenant.Identifier, tenant.Name);
    }
}

/// <summary>Validates granted roles/branches: they exist in the tenant, are in the caller's scope, and grant nothing
/// the caller does not hold (privilege escalation guard).</summary>
public sealed class MembershipGrantValidator(TenancyDbContext dbContext, TenancyRequest request)
{
    public async Task EnsureValidAsync(IReadOnlyCollection<Guid> roleIds, IReadOnlyCollection<BranchAssignment> branches,
        CancellationToken cancellationToken)
    {
        var distinctRoles = roleIds.Distinct().ToList();
        var roles = await dbContext.Roles.AsNoTracking().Where(r => distinctRoles.Contains(r.Id)).ToListAsync(cancellationToken);
        if (roles.Count != distinctRoles.Count)
            throw new InvalidMembershipException("One or more roles do not exist.");
        request.EnsureCanGrant(roles.SelectMany(r => r.EffectivePermissions));

        var branchIds = branches.Select(b => b.BranchId).Distinct().ToList();
        var found = await dbContext.Branches.CountAsync(b => branchIds.Contains(b.Id), cancellationToken);
        if (found != branchIds.Count)
            throw new InvalidMembershipException("One or more branches do not exist.");
        request.EnsureBranchesInScope(branchIds);
    }
}
