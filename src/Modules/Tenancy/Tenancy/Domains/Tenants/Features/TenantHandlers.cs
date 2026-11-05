using Microsoft.Extensions.Options;
using Tenancy.Services;

namespace Tenancy.Domains.Tenants.Features;

// ============================================================================================================
// Create tenant (self-service sign-up or platform admin)
// ============================================================================================================

public class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Tenant.NameMaxLength);
        RuleFor(x => x.Identifier)
            .NotEmpty()
            .Must(id => Tenant.IsValidIdentifier(Tenant.NormalizeIdentifier(id)))
            .WithMessage($"Identifier must be {Tenant.IdentifierMinLength}-{Tenant.IdentifierMaxLength} characters: " +
                         "lowercase letters, digits and '-' (not at the start or end).")
            .Must(id => !Tenant.ReservedIdentifiers.Contains(Tenant.NormalizeIdentifier(id)))
            .WithMessage("This identifier is reserved.");
        RuleFor(x => x.ConnectionSecretRef).MaximumLength(128);
        RuleFor(x => x.Region).MaximumLength(64);
    }
}

/// <summary>
/// Creates the tenant with its system roles, head office and the owner membership (one transaction, host scope),
/// then provisioning runs in the background (<see cref="TenantCreatedEventHandler"/>). Self-service callers get the
/// default isolation and own the tenant; dedicated databases / foreign owners are platform-admin only.
/// </summary>
public class CreateTenantHandler(
    TenancyDbContext dbContext,
    IHttpContextService httpContextService,
    IOptions<MultiTenancyOptions> options,
    ITenantAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<CreateTenantCommand, CreateTenantResult>
{
    public async Task<CreateTenantResult> Handle(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
            throw new TenantAccessDeniedException(TenantErrors.MultiTenancyDisabled,
                "multi-tenancy is disabled (MultiTenancy:Enabled=false).");

        var callerId = httpContextService.GetUserIdAsGuid()
                       ?? throw new UnauthorizedException("Authentication is required.");
        var isPlatformAdmin = httpContextService.HasClaim(Permissions.ClaimType, Permissions.TenantsManage);

        if (!isPlatformAdmin)
        {
            if (!opts.AllowSelfServiceSignup)
                throw new ForbiddenException("Self-service tenant creation is disabled.");
            if (command.Isolation == TenantIsolation.Dedicated || command.ConnectionSecretRef is not null ||
                (command.OwnerUserId is { } other && other != callerId))
                throw new ForbiddenException("Dedicated databases and creating tenants for others require a platform admin.");
        }

        var owner = isPlatformAdmin && command.OwnerUserId is { } requestedOwner ? requestedOwner : callerId;
        var isolation = command.Isolation ?? opts.DefaultIsolation;
        var identifier = Tenant.NormalizeIdentifier(command.Identifier);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        using (TenantScope.BeginHostScope())
        {
            if (await dbContext.Tenants.AnyAsync(t => t.Identifier == identifier, cancellationToken))
                throw new TenantIdentifierAlreadyExistsException(identifier);

            var tenant = Tenant.Create(identifier, command.Name, owner, isolation, command.ConnectionSecretRef,
                command.Region, command.Culture, command.TimeZone);
            dbContext.Tenants.Add(tenant);

            foreach (var definition in SystemTenantRoles.All)
                dbContext.Roles.Add(TenantRole.CreateSystem(tenant.Id, definition));

            var headOffice = Branch.Create("HQ", "Head Office", null, opts.MaxBranchDepth, isHeadOffice: true);
            headOffice.TenantId = tenant.Id;
            dbContext.Branches.Add(headOffice);

            dbContext.Memberships.Add(TenantMembership.Create(tenant.Id, owner,
                [SystemTenantRoles.IdOf(tenant.Id, SystemTenantRoles.Admin)], [], now, isOwner: true));

            audit.Record(TenantAuditActions.TenantCreated, nameof(Tenant), tenant.Id.ToString(),
                new { tenant.Identifier, tenant.Name, Isolation = isolation.ToString(), owner }, tenant.Id);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation(Data.Configurations.TenantConfiguration.IdentifierIndexName))
            {
                throw new TenantIdentifierAlreadyExistsException(identifier);
            }

            return new CreateTenantResult(tenant.Id, tenant.Identifier, tenant.Status);
        }
    }
}

// ============================================================================================================
// Current tenant
// ============================================================================================================

public class GetCurrentTenantHandler(TenancyDbContext dbContext, TenancyRequest request)
    : IQueryHandler<GetCurrentTenantQuery, TenantDto>
{
    public async Task<TenantDto> Handle(GetCurrentTenantQuery query, CancellationToken cancellationToken)
    {
        var tenantId = request.TenantId;
        return await dbContext.Tenants.AsNoTracking()
                   .Where(t => t.Id == tenantId)
                   .Select(t => new TenantDto(t.Id, t.Identifier, t.Name, t.LegalName, t.Status, t.Isolation, t.Region,
                       t.Culture, t.TimeZone, t.Currency, t.LogoAttachmentId, t.PrimaryColor, t.OwnerUserId,
                       t.CreatedAt ?? DateTime.MinValue, t.Version))
                   .FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException(nameof(Tenant), tenantId);
    }
}

public class UpdateTenantSettingsCommandValidator : AbstractValidator<UpdateTenantSettingsCommand>
{
    public UpdateTenantSettingsCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Tenant.NameMaxLength);
        RuleFor(x => x.LegalName).MaximumLength(Tenant.NameMaxLength);
        RuleFor(x => x.Culture).MaximumLength(16);
        RuleFor(x => x.TimeZone).MaximumLength(64);
        RuleFor(x => x.Currency).Length(3).When(x => !string.IsNullOrEmpty(x.Currency));
        RuleFor(x => x.PrimaryColor).Matches("^#[0-9a-fA-F]{6}$").When(x => !string.IsNullOrEmpty(x.PrimaryColor));
    }
}

public class UpdateTenantSettingsHandler(TenancyDbContext dbContext, TenancyRequest request, ITenantAuditWriter audit)
    : ICommandHandler<UpdateTenantSettingsCommand, UpdateTenantSettingsResult>
{
    public async Task<UpdateTenantSettingsResult> Handle(UpdateTenantSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var tenant = await TenantLoader.CurrentAsync(dbContext, request.TenantId, cancellationToken);
        dbContext.ExpectVersion(tenant, command.Version);

        tenant.UpdateProfile(command.Name, command.LegalName, command.Culture, command.TimeZone, command.Currency,
            command.LogoAttachmentId, command.PrimaryColor);
        audit.Record(TenantAuditActions.TenantUpdated, nameof(Tenant), tenant.Id.ToString(), command);

        await dbContext.SaveChangesAsync(cancellationToken);
        return new UpdateTenantSettingsResult(tenant.Id);
    }
}

// ----- custom domains -----

public class GetTenantDomainsHandler(TenancyDbContext dbContext, TenancyRequest request)
    : IQueryHandler<GetTenantDomainsQuery, IReadOnlyList<TenantDomainDto>>
{
    public async Task<IReadOnlyList<TenantDomainDto>> Handle(GetTenantDomainsQuery query, CancellationToken cancellationToken)
    {
        var tenant = await TenantLoader.CurrentAsync(dbContext, request.TenantId, cancellationToken, tracking: false);
        return tenant.Domains.Select(TenantLoader.ToDto).ToList();
    }
}

public class AddTenantDomainCommandValidator : AbstractValidator<AddTenantDomainCommand>
{
    public AddTenantDomainCommandValidator() =>
        RuleFor(x => x.Host).NotEmpty().MaximumLength(TenantDomain.HostMaxLength);
}

public class AddTenantDomainHandler(TenancyDbContext dbContext, TenancyRequest request, ITenantAuditWriter audit)
    : ICommandHandler<AddTenantDomainCommand, TenantDomainDto>
{
    public async Task<TenantDomainDto> Handle(AddTenantDomainCommand command, CancellationToken cancellationToken)
    {
        var tenant = await TenantLoader.CurrentAsync(dbContext, request.TenantId, cancellationToken);
        var domain = tenant.AddDomain(command.Host);
        audit.Record(TenantAuditActions.DomainAdded, nameof(TenantDomain), domain.Host);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(Data.Configurations.TenantConfiguration.DomainHostIndexName))
        {
            throw new ConflictException($"'{domain.Host}' is already used by another tenant.");
        }

        return TenantLoader.ToDto(domain);
    }
}

public class VerifyTenantDomainHandler(
    TenancyDbContext dbContext,
    TenancyRequest request,
    ITenantDomainVerifier verifier,
    ITenantAuditWriter audit,
    TimeProvider timeProvider) : ICommandHandler<VerifyTenantDomainCommand, TenantDomainDto>
{
    public async Task<TenantDomainDto> Handle(VerifyTenantDomainCommand command, CancellationToken cancellationToken)
    {
        var tenant = await TenantLoader.CurrentAsync(dbContext, request.TenantId, cancellationToken);
        var host = TenantDomain.NormalizeHost(command.Host);
        var domain = tenant.Domains.FirstOrDefault(d => d.Host == host)
                     ?? throw new NotFoundException($"Domain '{host}' is not registered for this tenant.");

        if (!domain.IsVerified)
        {
            if (!await verifier.HasTxtRecordAsync(domain.VerificationRecordName, domain.VerificationToken, cancellationToken))
                throw new BadRequestException(
                    $"TXT record {domain.VerificationRecordName} = {domain.VerificationToken} was not found (DNS changes can take a while).");

            tenant.VerifyDomain(host, timeProvider.GetUtcNow().UtcDateTime);
            audit.Record(TenantAuditActions.DomainVerified, nameof(TenantDomain), host);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return TenantLoader.ToDto(domain);
    }
}

public class RemoveTenantDomainHandler(TenancyDbContext dbContext, TenancyRequest request, ITenantAuditWriter audit)
    : ICommandHandler<RemoveTenantDomainCommand, RemoveTenantDomainResult>
{
    public async Task<RemoveTenantDomainResult> Handle(RemoveTenantDomainCommand command, CancellationToken cancellationToken)
    {
        var tenant = await TenantLoader.CurrentAsync(dbContext, request.TenantId, cancellationToken);
        var host = TenantDomain.NormalizeHost(command.Host);
        tenant.RemoveDomain(host);
        audit.Record(TenantAuditActions.DomainRemoved, nameof(TenantDomain), host);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new RemoveTenantDomainResult(host);
    }
}

// ----- ownership & deletion (owner only) -----

public class TransferTenantOwnershipCommandValidator : AbstractValidator<TransferTenantOwnershipCommand>
{
    public TransferTenantOwnershipCommandValidator() => RuleFor(x => x.NewOwnerUserId).NotEmpty();
}

public class TransferTenantOwnershipHandler(TenancyDbContext dbContext, TenancyRequest request, ITenantAuditWriter audit)
    : ICommandHandler<TransferTenantOwnershipCommand, TransferTenantOwnershipResult>
{
    public async Task<TransferTenantOwnershipResult> Handle(TransferTenantOwnershipCommand command,
        CancellationToken cancellationToken)
    {
        request.EnsureOwner();
        var tenant = await TenantLoader.CurrentAsync(dbContext, request.TenantId, cancellationToken);

        var target = await dbContext.Memberships.FirstOrDefaultAsync(
                         m => m.UserId == command.NewOwnerUserId && m.Status == TenantMembershipStatus.Active,
                         cancellationToken)
                     ?? throw new InvalidMembershipException("The new owner must be an active member of this tenant.");

        var currentOwners = await dbContext.Memberships.Where(m => m.IsOwner && m.UserId != target.UserId)
            .ToListAsync(cancellationToken);
        foreach (var previous in currentOwners)
            previous.RevokeOwnership();

        target.GrantOwnership();
        tenant.TransferOwnership(target.UserId);
        audit.Record(TenantAuditActions.OwnershipTransferred, nameof(Tenant), tenant.Id.ToString(),
            new { From = currentOwners.Select(o => o.UserId), To = target.UserId });

        await dbContext.SaveChangesAsync(cancellationToken);
        return new TransferTenantOwnershipResult(tenant.Id, target.UserId);
    }
}

public class RequestTenantDeletionHandler(
    TenancyDbContext dbContext,
    TenancyRequest request,
    ITenantAuditWriter audit,
    IOptions<MultiTenancyOptions> options,
    TimeProvider timeProvider) : ICommandHandler<RequestTenantDeletionCommand, RequestTenantDeletionResult>
{
    public async Task<RequestTenantDeletionResult> Handle(RequestTenantDeletionCommand command,
        CancellationToken cancellationToken)
    {
        request.EnsureOwner();
        var tenant = await TenantLoader.CurrentAsync(dbContext, request.TenantId, cancellationToken);
        tenant.RequestDeletion(timeProvider.GetUtcNow().UtcDateTime, options.Value.DeletionGracePeriodDays, command.Reason);
        audit.Record(TenantAuditActions.TenantDeletionRequested, nameof(Tenant), tenant.Id.ToString(),
            new { command.Reason, tenant.PurgeAfter });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new RequestTenantDeletionResult(tenant.Id, tenant.PurgeAfter!.Value);
    }
}

// ============================================================================================================
// My tenants (tenant switcher)
// ============================================================================================================

/// <summary>
/// The caller's tenants. "Current" = the tenant of the caller's SESSION (token claim <c>tid</c>): the endpoint is
/// host-scoped (works with any session), so the ambient tenant is empty and cannot be used.
/// </summary>
public class GetUserTenantsHandler(TenancyDbContext dbContext, IHttpContextService httpContextService)
    : IQueryHandler<GetUserTenantsQuery, IReadOnlyList<UserTenantDto>>
{
    public async Task<IReadOnlyList<UserTenantDto>> Handle(GetUserTenantsQuery query, CancellationToken cancellationToken)
    {
        Guid? current = Guid.TryParse(httpContextService.GetClaimValue(TenantClaimTypes.TenantId), out var tid) ? tid : null;

        using (TenantScope.BeginHostScope())
        {
            var rows = await dbContext.Memberships.AsNoTracking()
                .Where(m => m.UserId == query.UserId && m.Status == TenantMembershipStatus.Active)
                .Join(dbContext.Tenants.Where(t => t.Status != TenantStatus.Deleted && t.Status != TenantStatus.PendingDeletion),
                    m => m.TenantId, t => t.Id,
                    (m, t) => new { t.Id, t.Identifier, t.Name, t.Status, m.IsOwner, m.RoleIds })
                .OrderBy(x => x.Name)
                .ToListAsync(cancellationToken);

            var roleIds = rows.SelectMany(r => r.RoleIds).Distinct().ToList();
            var roleNames = await dbContext.Roles.AsNoTracking()
                .Where(r => roleIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

            return rows.Select(r => new UserTenantDto(r.Id, r.Identifier, r.Name, r.Status, r.IsOwner,
                    r.RoleIds.Where(roleNames.ContainsKey).Select(id => roleNames[id]).ToList(),
                    r.Id == current))
                .ToList();
        }
    }
}

public class GetTenantByIdHandler(TenancyDbContext dbContext) : IQueryHandler<GetTenantByIdQuery, TenantDto?>
{
    public async Task<TenantDto?> Handle(GetTenantByIdQuery query, CancellationToken cancellationToken) =>
        await dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == query.TenantId)
            .Select(t => new TenantDto(t.Id, t.Identifier, t.Name, t.LegalName, t.Status, t.Isolation, t.Region,
                t.Culture, t.TimeZone, t.Currency, t.LogoAttachmentId, t.PrimaryColor, t.OwnerUserId,
                t.CreatedAt ?? DateTime.MinValue, t.Version))
            .FirstOrDefaultAsync(cancellationToken);
}

internal static class TenantLoader
{
    public static async Task<Tenant> CurrentAsync(TenancyDbContext db, Guid tenantId, CancellationToken ct,
        bool tracking = true)
    {
        var query = db.Tenants.Where(t => t.Id == tenantId);
        if (!tracking)
            query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(ct) ?? throw new NotFoundException(nameof(Tenant), tenantId);
    }

    public static TenantDomainDto ToDto(TenantDomain d) =>
        new(d.Host, d.IsVerified, d.VerifiedAt, d.VerificationRecordName, d.VerificationToken);
}
