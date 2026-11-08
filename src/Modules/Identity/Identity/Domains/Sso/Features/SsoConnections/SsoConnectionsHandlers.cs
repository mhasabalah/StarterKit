using Identity.Contracts.Sso;
using Identity.Data.Repository.Sso;
using Identity.Domains.Sso.Exceptions;
using Identity.Domains.Sso.Models;
using Identity.Options;
using Identity.Services.Sso;
using Microsoft.Extensions.Options;
using Npgsql;
using Shared.Core.MultiTenancy;
using Shared.Core.Net;
using Shared.Core.Pagination;

namespace Identity.Domains.Sso.Features.SsoConnections;

/// <summary>Validates the write model. URL acceptability (HTTPS, no credentials) depends on options.</summary>
public sealed class SsoConnectionInputValidator : AbstractValidator<SsoConnectionInput>
{
    public SsoConnectionInputValidator(IOptions<SsoOptions> options)
    {
        var insecure = options.Value.AllowInsecureIdentityProviders;

        RuleFor(x => x.Name).NotEmpty().MaximumLength(SsoConnection.NameMaxLength);
        RuleFor(x => x.Protocol).IsInEnum();

        RuleFor(x => x.OidcAuthority)
            .Must(u => u is null || SsoHttp.IsAcceptableUrl(u, insecure))
            .WithMessage("The OIDC authority must be an absolute HTTPS URL.")
            .When(x => x.Protocol == SsoProtocol.Oidc);
        RuleFor(x => x.OidcClientId).MaximumLength(512);
        RuleFor(x => x.OidcClientSecret).MaximumLength(1024);
        RuleFor(x => x.OidcExtraScopes).MaximumLength(512)
            .Matches("^[A-Za-z0-9_:./ -]*$").WithMessage("Scopes may only contain letters, digits and _:./-");

        RuleFor(x => x.SamlIdpSsoUrl)
            .Must(u => u is null || SsoHttp.IsAcceptableUrl(u, insecure))
            .WithMessage("The IdP single sign-on URL must be an absolute HTTPS URL.")
            .When(x => x.Protocol == SsoProtocol.Saml);
        RuleFor(x => x.SamlIdpMetadataUrl)
            .Must(u => u is null || SsoHttp.IsAcceptableUrl(u, insecure))
            .WithMessage("The IdP metadata URL must be an absolute HTTPS URL.")
            .When(x => x.Protocol == SsoProtocol.Saml);
        RuleFor(x => x.SamlIdpEntityId).MaximumLength(SsoConnection.UrlMaxLength);
        RuleFor(x => x.SamlIdpCertificates!.Count).LessThanOrEqualTo(SsoConnection.MaxCertificates)
            .When(x => x.SamlIdpCertificates is not null);
        RuleFor(x => x.SamlMetadataSigningCertificates!.Count).LessThanOrEqualTo(SsoConnection.MaxCertificates)
            .When(x => x.SamlMetadataSigningCertificates is not null);

        RuleFor(x => x.EmailClaim).MaximumLength(SsoConnection.ClaimMaxLength);
        RuleFor(x => x.GivenNameClaim).MaximumLength(SsoConnection.ClaimMaxLength);
        RuleFor(x => x.FamilyNameClaim).MaximumLength(SsoConnection.ClaimMaxLength);
        RuleFor(x => x.DefaultRoleIds!.Count).LessThanOrEqualTo(SsoConnection.MaxDefaultRoles)
            .When(x => x.DefaultRoleIds is not null);
    }
}

public sealed class CreateSsoConnectionCommandValidator : AbstractValidator<CreateSsoConnectionCommand>
{
    public CreateSsoConnectionCommandValidator(IOptions<SsoOptions> options) =>
        RuleFor(x => x.Input).NotNull().SetValidator(new SsoConnectionInputValidator(options));
}

public sealed class UpdateSsoConnectionCommandValidator : AbstractValidator<UpdateSsoConnectionCommand>
{
    public UpdateSsoConnectionCommandValidator(IOptions<SsoOptions> options)
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Input).NotNull().SetValidator(new SsoConnectionInputValidator(options));
    }
}

public sealed class CreateSsoConnectionHandler(
    ISsoConnectionRepository connections,
    ISsoSecretProtector secrets,
    ITenantContext tenant,
    ITenantRoleGuard roles,
    SsoConnectionMapper mapper) : ICommandHandler<CreateSsoConnectionCommand, SsoConnectionDto>
{
    public async Task<SsoConnectionDto> Handle(CreateSsoConnectionCommand command, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new TenantNotResolvedException();
        await roles.EnsureTenantRolesAsync(command.Input.DefaultRoleIds ?? [], cancellationToken);
        var secret = string.IsNullOrWhiteSpace(command.Input.OidcClientSecret)
            ? null
            : secrets.Protect(command.Input.OidcClientSecret.Trim());

        var connection = SsoConnection.Create(tenantId, command.Input, secret);
        connections.Add(connection);
        await connections.SaveAsync(cancellationToken);
        return mapper.ToDto(connection);
    }
}

public sealed class UpdateSsoConnectionHandler(
    ISsoConnectionRepository connections,
    ApplicationIdentityDbContext dbContext,
    ISsoSecretProtector secrets,
    ITenantContext tenant,
    ITenantRoleGuard roles,
    SsoConnectionMapper mapper) : ICommandHandler<UpdateSsoConnectionCommand, SsoConnectionDto>
{
    public async Task<SsoConnectionDto> Handle(UpdateSsoConnectionCommand command, CancellationToken cancellationToken)
    {
        var connection = await SsoConnectionLoader.LoadAsync(connections, tenant, command.Id, cancellationToken);
        dbContext.ExpectVersion(connection, command.Version);
        await roles.EnsureTenantRolesAsync(command.Input.DefaultRoleIds ?? [], cancellationToken);

        var secret = string.IsNullOrWhiteSpace(command.Input.OidcClientSecret)
            ? null
            : secrets.Protect(command.Input.OidcClientSecret.Trim());
        connection.Update(command.Input, secret);

        await connections.SaveAsync(cancellationToken);
        return mapper.ToDto(connection);
    }
}

public sealed class ActivateSsoConnectionHandler(
    ISsoConnectionRepository connections,
    ApplicationIdentityDbContext dbContext,
    ITenantContext tenant,
    SsoConnectionMapper mapper) : ICommandHandler<ActivateSsoConnectionCommand, SsoConnectionDto>
{
    public async Task<SsoConnectionDto> Handle(ActivateSsoConnectionCommand command, CancellationToken cancellationToken)
    {
        var connection = await SsoConnectionLoader.LoadAsync(connections, tenant, command.Id, cancellationToken);
        dbContext.ExpectVersion(connection, command.Version);
        connection.Activate();
        await connections.SaveAsync(cancellationToken);
        return mapper.ToDto(connection);
    }
}

public sealed class DisableSsoConnectionHandler(
    ISsoConnectionRepository connections,
    ApplicationIdentityDbContext dbContext,
    ITenantContext tenant,
    SsoConnectionMapper mapper) : ICommandHandler<DisableSsoConnectionCommand, SsoConnectionDto>
{
    public async Task<SsoConnectionDto> Handle(DisableSsoConnectionCommand command, CancellationToken cancellationToken)
    {
        var connection = await SsoConnectionLoader.LoadAsync(connections, tenant, command.Id, cancellationToken);
        dbContext.ExpectVersion(connection, command.Version);
        connection.Disable();
        await connections.SaveAsync(cancellationToken);
        return mapper.ToDto(connection);
    }
}

/// <summary>
/// Deletes the connection. Linked sign-ins (<c>sso:{id}</c> user logins) stop working; accounts and memberships
/// stay (users fall back to their other sign-in methods or a password reset).
/// </summary>
public sealed class DeleteSsoConnectionHandler(
    ISsoConnectionRepository connections,
    ApplicationIdentityDbContext dbContext,
    ITenantContext tenant) : ICommandHandler<DeleteSsoConnectionCommand, DeleteSsoConnectionResult>
{
    public async Task<DeleteSsoConnectionResult> Handle(DeleteSsoConnectionCommand command, CancellationToken cancellationToken)
    {
        var connection = await SsoConnectionLoader.LoadAsync(connections, tenant, command.Id, cancellationToken);
        connection.Disable(); // raises the change event (cache invalidation) before the row goes
        connections.Remove(connection);

        await connections.SaveAsync(cancellationToken);

        // After the delete committed: a failed save never leaves orphaned-but-deleted links behind. Leftover links
        // after a crash here are inert (no connection can match them) and are removed by a retry of this delete.
        var provider = SsoAccountService.LoginProvider(connection.Id);
        await dbContext.UserLogins.Where(l => l.LoginProvider == provider).ExecuteDeleteAsync(cancellationToken);
        return new DeleteSsoConnectionResult(connection.Id);
    }
}

public sealed class RefreshSamlMetadataCommandValidator : AbstractValidator<RefreshSamlMetadataCommand>
{
    public RefreshSamlMetadataCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

/// <summary>
/// "Refresh now" for an SAML connection with a metadata URL. A failed refresh is an outcome, not an error: it is
/// recorded on the connection and reported in the result.
/// </summary>
public sealed class RefreshSamlMetadataHandler(
    ISsoConnectionRepository connections,
    ITenantContext tenant,
    SamlMetadataRefresher refresher) : ICommandHandler<RefreshSamlMetadataCommand, RefreshSamlMetadataResult>
{
    public async Task<RefreshSamlMetadataResult> Handle(RefreshSamlMetadataCommand command, CancellationToken cancellationToken)
    {
        var connection = await SsoConnectionLoader.LoadAsync(connections, tenant, command.Id, cancellationToken);
        if (connection.Protocol != SsoProtocol.Saml)
            throw new InvalidSsoConnectionException("Sso.Connection.NotSaml");
        if (string.IsNullOrWhiteSpace(connection.SamlIdpMetadataUrl))
            throw new InvalidSsoConnectionException("Sso.Connection.MetadataUrlRequired");

        var result = await refresher.RefreshAsync(connection, cancellationToken);
        return new RefreshSamlMetadataResult(connection.Id, result.Outcome.ToString(), result.Error, result.RefreshedAt);
    }
}

public sealed class AddSsoDomainCommandValidator : AbstractValidator<AddSsoDomainCommand>
{
    public AddSsoDomainCommandValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty();
        RuleFor(x => x.Domain).NotEmpty().MaximumLength(253);
    }
}

public sealed class AddSsoDomainHandler(
    ISsoConnectionRepository connections,
    ITenantContext tenant) : ICommandHandler<AddSsoDomainCommand, SsoDomainDto>
{
    public async Task<SsoDomainDto> Handle(AddSsoDomainCommand command, CancellationToken cancellationToken)
    {
        var connection = await SsoConnectionLoader.LoadAsync(connections, tenant, command.ConnectionId, cancellationToken);
        var domain = connection.AddDomain(command.Domain);

        if (await connections.IsDomainVerifiedElsewhereAsync(domain.Domain, connection.Id, cancellationToken))
            throw new SsoDomainTakenException(domain.Domain);

        await connections.SaveAsync(cancellationToken);
        return SsoConnectionMapper.ToDto(domain);
    }
}

public sealed class VerifySsoDomainCommandValidator : AbstractValidator<VerifySsoDomainCommand>
{
    public VerifySsoDomainCommandValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty();
        RuleFor(x => x.Domain).NotEmpty().MaximumLength(253);
    }
}

/// <summary>
/// Checks <c>_starterkit-sso.{domain}</c> TXT = <c>sso-verification={token}</c>. A domain can be verified by one
/// connection platform-wide (also guarded by a filtered unique index for the race).
/// </summary>
public sealed class VerifySsoDomainHandler(
    ISsoConnectionRepository connections,
    IDnsTxtVerifier dns,
    ITenantContext tenant,
    TimeProvider timeProvider) : ICommandHandler<VerifySsoDomainCommand, SsoDomainDto>
{
    public async Task<SsoDomainDto> Handle(VerifySsoDomainCommand command, CancellationToken cancellationToken)
    {
        var connection = await SsoConnectionLoader.LoadAsync(connections, tenant, command.ConnectionId, cancellationToken);
        var domain = connection.GetDomain(command.Domain);
        if (domain.IsVerified)
            return SsoConnectionMapper.ToDto(domain);

        if (await connections.IsDomainVerifiedElsewhereAsync(domain.Domain, connection.Id, cancellationToken))
            throw new SsoDomainTakenException(domain.Domain);

        if (!await dns.HasTxtRecordAsync(domain.RecordName, domain.RecordValue, cancellationToken))
            throw new SsoDomainUnverifiedException(domain.RecordName);

        connection.MarkDomainVerified(domain.Domain, timeProvider.GetUtcNow().UtcDateTime);
        try
        {
            await connections.SaveAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new SsoDomainTakenException(domain.Domain);
        }

        return SsoConnectionMapper.ToDto(domain);
    }
}

public sealed class RemoveSsoDomainHandler(
    ISsoConnectionRepository connections,
    ITenantContext tenant) : ICommandHandler<RemoveSsoDomainCommand, RemoveSsoDomainResult>
{
    public async Task<RemoveSsoDomainResult> Handle(RemoveSsoDomainCommand command, CancellationToken cancellationToken)
    {
        var connection = await SsoConnectionLoader.LoadAsync(connections, tenant, command.ConnectionId, cancellationToken);
        var normalized = SsoDomain.Normalize(command.Domain);
        connection.RemoveDomain(normalized);
        await connections.SaveAsync(cancellationToken);
        return new RemoveSsoDomainResult(normalized);
    }
}

public sealed class GetSsoConnectionsHandler(
    ApplicationIdentityDbContext dbContext,
    ITenantContext tenant,
    SsoConnectionMapper mapper) : IQueryHandler<GetSsoConnectionsQuery, List<SsoConnectionDto>>
{
    public async Task<List<SsoConnectionDto>> Handle(GetSsoConnectionsQuery query, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new TenantNotResolvedException();
        var rows = await dbContext.SsoConnections.AsNoTracking().Include(c => c.Domains)
            .Where(c => c.TenantId == tenantId)
            .OrderBy(c => c.Name)
            .Take(50) // bounded: a tenant has a handful of connections
            .ToListAsync(cancellationToken);
        return rows.Select(mapper.ToDto).ToList();
    }
}

public sealed class GetSsoConnectionByIdHandler(
    ApplicationIdentityDbContext dbContext,
    ITenantContext tenant,
    SsoConnectionMapper mapper) : IQueryHandler<GetSsoConnectionByIdQuery, SsoConnectionDto>
{
    public async Task<SsoConnectionDto> Handle(GetSsoConnectionByIdQuery query, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new TenantNotResolvedException();
        var connection = await dbContext.SsoConnections.AsNoTracking().Include(c => c.Domains)
                             .FirstOrDefaultAsync(c => c.Id == query.Id && c.TenantId == tenantId, cancellationToken)
                         ?? throw new SsoConnectionNotFoundException(query.Id);
        return mapper.ToDto(connection);
    }
}

public sealed class GetAllSsoConnectionsQueryValidator : BaseQueryValidator<GetAllSsoConnectionsQuery>;

/// <summary>Platform staff overview across tenants (no secrets, no configuration details).</summary>
public sealed class GetAllSsoConnectionsHandler(ApplicationIdentityDbContext dbContext)
    : IQueryHandler<GetAllSsoConnectionsQuery, PaginatedResult<SsoConnectionSummaryDto>>
{
    public Task<PaginatedResult<SsoConnectionSummaryDto>> Handle(
        GetAllSsoConnectionsQuery query, CancellationToken cancellationToken)
    {
        var rows = dbContext.SsoConnections.AsNoTracking();
        if (query.TenantId is { } tenantId)
            rows = rows.Where(c => c.TenantId == tenantId);

        return rows
            .OrderBy(c => c.TenantId).ThenBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new SsoConnectionSummaryDto(
                c.Id, c.TenantId, c.Name, c.Protocol, c.Status, c.EnforceSso,
                c.Domains.Where(d => d.VerifiedAt != null).Select(d => d.Domain).ToList(),
                c.LastLoginAt))
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
    }
}

internal static class SsoConnectionLoader
{
    public static async Task<SsoConnection> LoadAsync(
        ISsoConnectionRepository connections, ITenantContext tenant, Guid id, CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new TenantNotResolvedException();
        return await connections.GetAsync(tenantId, id, ct) ?? throw new SsoConnectionNotFoundException(id);
    }
}

/// <summary>Aggregate → DTO, including the service-provider values the IdP admin needs.</summary>
public sealed class SsoConnectionMapper(SsoUrls urls)
{
    public SsoConnectionDto ToDto(SsoConnection c) => new(
        c.Id, c.Name, c.Protocol, c.Status,
        c.OidcAuthority, c.OidcClientId, c.OidcClientSecretProtected is not null, c.OidcExtraScopes,
        c.SamlIdpEntityId, c.SamlIdpSsoUrl, c.SamlIdpMetadataUrl,
        c.SamlIdpCertificates.Select(SsoCertificates.Describe).ToList(),
        c.SamlAllowIdpInitiated,
        c.SamlMetadataRefreshedAt, c.SamlMetadataLastError,
        c.SamlCertificatesNotAfter ?? (c.SamlIdpCertificates.Count == 0
            ? null
            : c.SamlIdpCertificates.Max(x => SsoCertificates.Describe(x).NotAfter)),
        c.SamlRequireSignedMetadata,
        c.SamlMetadataSigningCertificates.Select(SsoCertificates.Describe).ToList(),
        c.RequiresSignedMetadata,
        c.EmailClaim, c.GivenNameClaim, c.FamilyNameClaim,
        c.JitProvisioning, c.DefaultRoleIds, c.EnforceSso, c.TrustIdpMfa,
        c.Domains.OrderBy(d => d.Domain).Select(ToDto).ToList(),
        c.Protocol == SsoProtocol.Oidc
            ? new SsoServiceProviderDto(urls.OidcRedirectUri, null, null, null)
            : new SsoServiceProviderDto(null, urls.SamlEntityId(c.Id), urls.SamlAcsUrl(c.Id), urls.SamlMetadataUrl(c.Id)),
        c.LastLoginAt,
        c.Version);

    public static SsoDomainDto ToDto(SsoDomain d) =>
        new(d.Domain, d.IsVerified, d.RecordName, d.RecordValue, d.VerifiedAt);
}
