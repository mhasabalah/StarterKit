using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Sso;
using Identity.Data.Repository.Sso;
using Identity.Domains.Auth.Services;
using Identity.Domains.Sso.Exceptions;
using Identity.Domains.Sso.Models;
using Identity.Options;
using Identity.Services.Sso;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Shared.Core.Entitlements;
using Shared.Core.MultiTenancy;

namespace Identity.Domains.Sso.Features.SsoLogin;

public sealed class DiscoverSsoQueryValidator : AbstractValidator<DiscoverSsoQuery>
{
    public DiscoverSsoQueryValidator() => RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
}

/// <summary>
/// Home-realm discovery for an "email first" sign-in screen. It reveals only whether a DOMAIN uses SSO (public
/// knowledge for any company that does), never whether an account exists.
/// </summary>
public sealed class DiscoverSsoHandler(ISsoEnforcementPolicy policy) : IQueryHandler<DiscoverSsoQuery, SsoDiscoveryResult>
{
    public async Task<SsoDiscoveryResult> Handle(DiscoverSsoQuery query, CancellationToken cancellationToken)
    {
        var connection = await policy.FindConnectionForEmailAsync(query.Email, cancellationToken);
        return connection is null
            ? new SsoDiscoveryResult(false, false, null, null)
            : new SsoDiscoveryResult(true, connection.EnforceSso, connection.Id, connection.Protocol);
    }
}

public sealed class StartSsoLoginCommandValidator : AbstractValidator<StartSsoLoginCommand>
{
    public StartSsoLoginCommandValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty();
        RuleFor(x => x.ReturnUrl).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.LoginHint).MaximumLength(256);
    }
}

/// <summary>Creates the single-use login state and the redirect to the IdP.</summary>
public sealed class StartSsoLoginHandler(
    ISsoConnectionRepository connections,
    SsoLoginFlow flow) : ICommandHandler<StartSsoLoginCommand, SsoRedirect>
{
    public async Task<SsoRedirect> Handle(StartSsoLoginCommand command, CancellationToken cancellationToken)
    {
        flow.EnsureEnabled();
        if (!flow.ReturnUrls.IsAllowed(command.ReturnUrl))
            throw new BadRequestException("returnUrl is not an allowed application URL.");

        var connection = await connections.GetActiveAsync(command.ConnectionId, cancellationToken)
                         ?? throw new SsoConnectionNotFoundException(command.ConnectionId);

        // The tenant plan no longer includes "sso": behave as if the connection did not exist.
        if (!await flow.IsEntitledAsync(connection, cancellationToken))
            throw new SsoConnectionNotFoundException(command.ConnectionId);

        return await flow.ChallengeAsync(connection, command.ReturnUrl, command.LoginHint, testMode: false, cancellationToken);
    }
}

/// <summary>
/// OIDC redirect-URI callback. Never throws to the browser: every outcome is a redirect back to the application
/// (with <c>sso_code</c> on success, <c>sso_error</c> otherwise), so a user never lands on a raw API error page.
/// </summary>
public sealed class CompleteOidcLoginHandler(
    OidcSsoClient oidc,
    SsoLoginFlow flow,
    ILogger<CompleteOidcLoginHandler> logger) : ICommandHandler<CompleteOidcLoginCommand, SsoCallbackResult>
{
    public async Task<SsoCallbackResult> Handle(CompleteOidcLoginCommand command, CancellationToken cancellationToken)
    {
        var state = await flow.ConsumeStateAsync(command.State, cancellationToken);
        if (state is null)
            return flow.Fail(null, "state_invalid");

        if (!SsoLoginFlow.BindingMatches(state, command.BrowserBinding))
            return flow.Fail(state.ReturnUrl, "state_invalid");

        if (!string.IsNullOrEmpty(command.Error))
        {
            logger.LogWarning("OIDC IdP returned error {Error} for connection {ConnectionId}", command.Error, state.ConnectionId);
            return flow.Fail(state.ReturnUrl, "idp_error");
        }

        var connection = await flow.LoadForCallbackAsync(state, cancellationToken);
        if (connection is null || connection.Protocol != SsoProtocol.Oidc || string.IsNullOrEmpty(command.Code))
            return flow.Fail(state.ReturnUrl, "connection_unavailable");

        return await flow.FinishAsync(connection, state,
            ct => oidc.RedeemAsync(connection, command.Code!, state, ct), cancellationToken);
    }
}

/// <summary>SAML ACS (HTTP-POST). Same redirect-only contract as the OIDC callback.</summary>
public sealed class CompleteSamlLoginHandler(
    ISsoConnectionRepository connections,
    SamlSsoClient saml,
    SsoLoginFlow flow) : ICommandHandler<CompleteSamlLoginCommand, SsoCallbackResult>
{
    public async Task<SsoCallbackResult> Handle(CompleteSamlLoginCommand command, CancellationToken cancellationToken)
    {
        var stateId = SamlSsoClient.ReadRelayState(command.RelayState);
        var state = stateId is null ? null : await flow.ConsumeStateAsync(stateId, cancellationToken);

        if (state is not null &&
            (state.ConnectionId != command.ConnectionId || !SsoLoginFlow.BindingMatches(state, command.BrowserBinding)))
            return flow.Fail(state.ReturnUrl, "state_invalid");

        var connection = state is not null
            ? await flow.LoadForCallbackAsync(state, cancellationToken)
            : await connections.GetActiveAsync(command.ConnectionId, cancellationToken);
        if (connection is null || connection.Protocol != SsoProtocol.Saml)
            return flow.Fail(state?.ReturnUrl, "connection_unavailable");

        // IdP-initiated (no state): only when the tenant explicitly allowed it; lands on the app home page.
        if (state is null)
        {
            if (stateId is not null || !connection.SamlAllowIdpInitiated)
                return flow.Fail(null, "state_invalid");
            state = new SsoLoginState(connection.Id, flow.ReturnUrls.FallbackUrl);
        }

        var login = state;
        return await flow.FinishAsync(connection, login,
            ct => saml.ValidateResponseAsync(connection, command.SamlResponse, command.RelayState, login.SamlRequestId, ct),
            cancellationToken);
    }
}

public sealed class StartSsoTestCommandValidator : AbstractValidator<StartSsoTestCommand>
{
    public StartSsoTestCommandValidator()
    {
        RuleFor(x => x.ConnectionId).NotEmpty();
        RuleFor(x => x.ReturnUrl).NotEmpty().MaximumLength(2048);
    }
}

/// <summary>Tenant admin: IdP round trip for a connection of the CURRENT tenant, in test mode.</summary>
public sealed class StartSsoTestHandler(
    ISsoConnectionRepository connections,
    ITenantContext tenant,
    SsoLoginFlow flow) : ICommandHandler<StartSsoTestCommand, SsoRedirect>
{
    public async Task<SsoRedirect> Handle(StartSsoTestCommand command, CancellationToken cancellationToken)
    {
        flow.EnsureEnabled();
        if (!flow.ReturnUrls.IsAllowed(command.ReturnUrl))
            throw new BadRequestException("returnUrl is not an allowed application URL.");

        var tenantId = tenant.TenantId ?? throw new TenantNotResolvedException();
        var connection = await connections.GetAsync(tenantId, command.ConnectionId, cancellationToken)
                         ?? throw new SsoConnectionNotFoundException(command.ConnectionId);

        return await flow.ChallengeAsync(connection, command.ReturnUrl, null, testMode: true, cancellationToken);
    }
}

public sealed class GetSsoTestReportHandler(
    SsoOneTimeStore store,
    ISsoConnectionRepository connections,
    ITenantContext tenant) : IQueryHandler<GetSsoTestReportQuery, SsoTestReportDto>
{
    public async Task<SsoTestReportDto> Handle(GetSsoTestReportQuery query, CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new TenantNotResolvedException();
        var report = await store.ConsumeAsync<SsoTestReportDto>(
                         SsoLoginFlow.TestKind, query.Code, TimeSpan.FromMinutes(10), cancellationToken)
                     ?? throw new NotFoundException("The test result expired or was already read.");

        // A report is only readable by the tenant that owns the connection.
        _ = await connections.GetAsync(tenantId, report.ConnectionId, cancellationToken)
            ?? throw new NotFoundException("The test result expired or was already read.");
        return report;
    }
}

public sealed class GetSsoSamlMetadataHandler(ISsoConnectionRepository connections, SamlSsoClient saml)
    : IQueryHandler<GetSsoSamlMetadataQuery, string>
{
    public async Task<string> Handle(GetSsoSamlMetadataQuery query, CancellationToken cancellationToken)
    {
        var connection = await connections.GetByIdAsync(query.ConnectionId, cancellationToken);
        if (connection is null || connection.Protocol != SsoProtocol.Saml || connection.SamlIdpSsoUrl is null)
            throw new SsoConnectionNotFoundException(query.ConnectionId);
        return saml.BuildMetadataXml(connection);
    }
}

public sealed class ExchangeSsoCodeCommandValidator : AbstractValidator<ExchangeSsoCodeCommand>
{
    public ExchangeSsoCodeCommandValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(128);
}

/// <summary>
/// One-time code → session in the connection's tenant. When the account has its own TOTP 2FA and the IdP did not
/// assert MFA (or the connection does not trust it), the result is the usual 2FA challenge instead of tokens.
/// </summary>
public sealed class ExchangeSsoCodeHandler(
    SsoOneTimeStore store,
    UserManager<ApplicationUser> userManager,
    ITenantStore tenantStore,
    ILoginCompletionService loginCompletion,
    IOptions<SsoOptions> options) : ICommandHandler<ExchangeSsoCodeCommand, LoginResult>
{
    public const string InvalidCodeMessage = "The sign-in code is invalid or expired. Sign in again.";

    public async Task<LoginResult> Handle(ExchangeSsoCodeCommand command, CancellationToken cancellationToken)
    {
        var lifetime = TimeSpan.FromSeconds(Math.Clamp(options.Value.ExchangeCodeLifetimeSeconds, 10, 300));
        var grant = await store.ConsumeAsync<SsoLoginGrant>(SsoLoginFlow.GrantKind, command.Code, lifetime, cancellationToken)
                    ?? throw new UnauthorizedException(InvalidCodeMessage);

        var user = await userManager.FindByIdAsync(grant.UserId.ToString());
        if (user is null || !user.IsActive || await userManager.IsLockedOutAsync(user))
            throw new UnauthorizedException(InvalidCodeMessage);

        // The session (and every refresh of it) is bound to the tenant whose IdP vouched for the user.
        user.LastActiveTenantId = grant.TenantId;

        if (!grant.MultiFactor && await userManager.GetTwoFactorEnabledAsync(user))
        {
            await userManager.UpdateAsync(user);
            return await loginCompletion.ChallengeAsync(user, cancellationToken);
        }

        var tenant = await tenantStore.FindByIdAsync(grant.TenantId, cancellationToken)
                     ?? throw new UnauthorizedException(InvalidCodeMessage);
        using (TenantScope.Begin(tenant))
        {
            return await loginCompletion.CompleteAsync(user, grant.MultiFactor, cancellationToken);
        }
    }
}

/// <summary>The steps shared by both protocols: state, account resolution, grant, redirects and failure mapping.</summary>
public sealed class SsoLoginFlow(
    SsoOneTimeStore store,
    SsoAccountService accounts,
    OidcSsoClient oidc,
    SamlSsoClient saml,
    ISsoConnectionRepository connections,
    SsoReturnUrlPolicy returnUrls,
    ISsoEntitlement entitlement,
    IOptions<SsoOptions> options,
    TimeProvider timeProvider,
    ILogger<SsoLoginFlow> logger)
{
    public const string StateKind = "state";
    public const string GrantKind = "grant";
    public const string TestKind = "test";

    /// <summary>The only error codes a browser ever sees (anything else becomes <c>sso_failed</c>).</summary>
    private static readonly HashSet<string> PublicErrors = new(StringComparer.Ordinal)
    {
        "state_invalid", "idp_error", "connection_unavailable", "domain_not_allowed", "not_provisioned",
        "account_disabled", "tenant_unavailable", "membership_inactive", "sso_disabled", "staff_account",
        "access_denied", "provisioning_failed"
    };

    public SsoReturnUrlPolicy ReturnUrls => returnUrls;

    private TimeSpan StateLifetime => TimeSpan.FromMinutes(Math.Clamp(options.Value.LoginTimeoutMinutes, 2, 30));

    private TimeSpan CodeLifetime => TimeSpan.FromSeconds(Math.Clamp(options.Value.ExchangeCodeLifetimeSeconds, 10, 300));

    public void EnsureEnabled()
    {
        if (!options.Value.Enabled)
            throw new NotFoundException("Single sign-on is not enabled.");
    }

    /// <summary>The connection tenant still has the plan feature <c>sso</c> (evaluated in that tenant scope).</summary>
    public Task<bool> IsEntitledAsync(SsoConnection connection, CancellationToken ct) =>
        entitlement.IsEntitledAsync(connection.TenantId, ct);

    /// <summary>True when the state is not browser-bound (test mode) or the cookie value hashes to the stored hash.</summary>
    public static bool BindingMatches(SsoLoginState state, string? browserBinding) =>
        state.BrowserBindingHash is null ||
        (browserBinding is { Length: > 0 and < 200 } &&
         CryptographicOperations.FixedTimeEquals(
             Encoding.ASCII.GetBytes(Hash(browserBinding)), Encoding.ASCII.GetBytes(state.BrowserBindingHash)));

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<SsoRedirect> ChallengeAsync(
        SsoConnection connection, string returnUrl, string? loginHint, bool testMode, CancellationToken ct)
    {
        var stateId = SsoOneTimeStore.NewId();

        // Login-CSRF defence: a real sign-in only completes in the browser holding this cookie value. Test round
        // trips sign nobody in, so they are not bound (the admin may open the URL in any browser).
        var binding = testMode ? null : SsoOneTimeStore.NewId();
        var bindingHash = binding is null ? null : Hash(binding);
        SsoLoginState state;
        SsoRedirect redirect;

        if (connection.Protocol == SsoProtocol.Oidc)
        {
            state = new SsoLoginState(connection.Id, returnUrl, OidcSsoClient.NewNonce(), OidcSsoClient.NewCodeVerifier(),
                TestMode: testMode, BrowserBindingHash: bindingHash);
            redirect = await oidc.BuildAuthorizationRedirectAsync(connection, stateId, state, loginHint, ct);
        }
        else
        {
            redirect = saml.BuildAuthnRequest(connection, stateId, out var requestId);
            state = new SsoLoginState(connection.Id, returnUrl, SamlRequestId: requestId, TestMode: testMode,
                BrowserBindingHash: bindingHash);
        }

        await store.StoreAsync(StateKind, stateId, state, StateLifetime, ct);
        return redirect with { BrowserBinding = binding };
    }

    public Task<SsoLoginState?> ConsumeStateAsync(string? stateId, CancellationToken ct) =>
        store.ConsumeAsync<SsoLoginState>(StateKind, stateId, StateLifetime, ct);

    /// <summary>Sign-in needs an ACTIVE connection; a test round trip may target a draft or disabled one.</summary>
    public async Task<SsoConnection?> LoadForCallbackAsync(SsoLoginState state, CancellationToken ct) =>
        state.TestMode
            ? await connections.GetByIdAsync(state.ConnectionId, ct)
            : await connections.GetActiveAsync(state.ConnectionId, ct);

    public async Task<SsoCallbackResult> FinishAsync(
        SsoConnection connection, SsoLoginState state, Func<CancellationToken, Task<SsoIdentity>> validate,
        CancellationToken ct)
    {
        try
        {
            var identity = await validate(ct);

            if (state.TestMode)
            {
                var report = new SsoTestReportDto(connection.Id, identity.Subject, identity.Email,
                    identity.Email is not null && connection.CoversVerified(identity.Email), identity.MultiFactor,
                    identity.GivenName, identity.FamilyName, identity.Claims);
                var testCode = await store.CreateAsync(TestKind, report, TimeSpan.FromMinutes(10), ct);
                return new SsoCallbackResult(SsoReturnUrlPolicy.AppendQuery(state.ReturnUrl, "sso_test", testCode));
            }

            if (!await IsEntitledAsync(connection, ct))
                return Fail(state.ReturnUrl, "sso_disabled");

            var user = await accounts.ResolveAsync(connection, identity, ct);
            await connections.TouchLastLoginAsync(connection.Id, timeProvider.GetUtcNow().UtcDateTime, ct);

            var grant = new SsoLoginGrant(user.Id, connection.TenantId, connection.Id,
                MultiFactor: connection.TrustIdpMfa && identity.MultiFactor);
            var code = await store.CreateAsync(GrantKind, grant, CodeLifetime, ct);

            logger.LogInformation("SSO sign-in of user {UserId} through connection {ConnectionId}", user.Id, connection.Id);
            return new SsoCallbackResult(SsoReturnUrlPolicy.AppendQuery(state.ReturnUrl, "sso_code", code));
        }
        catch (SsoLoginFailedException ex)
        {
            logger.LogWarning("SSO sign-in failed for connection {ConnectionId}: {Reason}", connection.Id, ex.Reason);
            return Fail(state.ReturnUrl, ex.Reason);
        }
        catch (ForbiddenException ex)
        {
            // e.g. the plan MaxUsers when provisioning a new member: still a redirect, never a JSON page.
            logger.LogWarning(ex, "SSO sign-in refused for connection {ConnectionId}", connection.Id);
            return Fail(state.ReturnUrl, "access_denied");
        }
        catch (Exception ex) when (ex is CustomException or DbUpdateException)
        {
            logger.LogError(ex, "SSO provisioning failed for connection {ConnectionId}", connection.Id);
            return Fail(state.ReturnUrl, "provisioning_failed");
        }
    }

    public SsoCallbackResult Fail(string? returnUrl, string reason)
    {
        var code = PublicErrors.Contains(reason) ? reason : "sso_failed";
        var target = returnUrl is not null && returnUrls.IsAllowed(returnUrl) ? returnUrl : returnUrls.FallbackUrl;
        return new SsoCallbackResult(SsoReturnUrlPolicy.AppendQuery(target, "sso_error", code));
    }
}
