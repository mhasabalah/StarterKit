using Identity.Contracts.Sso;
using Identity.Domains.Sso.Features.SsoLogin;
using Identity.Options;
using Identity.Services.Sso;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsOptions = Microsoft.Extensions.Options.Options;
using Shared.Core.Exceptions;

namespace Identity.Tests.Sso;

/// <summary>
/// The SSO master switch (<c>Identity:Sso:Enabled</c>) is checked when a sign-in starts AND when it finishes: a
/// sign-in started before an operator switched SSO off must not complete afterwards (the state lives up to
/// <c>LoginTimeoutMinutes</c>, the exchange code up to <c>ExchangeCodeLifetimeSeconds</c>).
/// </summary>
public class SsoKillSwitchTests
{
    private const string Frontend = "https://app.example.com";

    private static IOptions<SsoOptions> Disabled() => MsOptions.Create(new SsoOptions { Enabled = false });

    private static SsoLoginFlow Flow(IOptions<SsoOptions> sso) =>
        // With SSO off FinishAsync returns before it touches the store, the IdP clients, accounts or the entitlement.
        new(store: null!, accounts: null!, oidc: null!, saml: null!, connections: null!,
            returnUrls: new SsoReturnUrlPolicy(sso, MsOptions.Create(new ApplicationOptions { FrontendBaseUrl = Frontend })),
            entitlement: null!, options: sso, timeProvider: TimeProvider.System, logger: NullLogger<SsoLoginFlow>.Instance);

    [Fact]
    public async Task Callback_after_sso_was_switched_off_fails_with_sso_disabled_and_never_calls_the_idp()
    {
        var idpCalled = false;
        var state = new SsoLoginState(Guid.NewGuid(), $"{Frontend}/after-login");

        var result = await Flow(Disabled()).FinishAsync(connection: null!, state, _ =>
        {
            idpCalled = true;
            throw new InvalidOperationException("the IdP must not be called while SSO is off");
        }, CancellationToken.None);

        idpCalled.Should().BeFalse();
        result.RedirectUrl.Should().StartWith($"{Frontend}/after-login").And.Contain("sso_error=sso_disabled")
            .And.NotContain("sso_code", "no exchange code may be issued while SSO is off");
    }

    [Fact]
    public async Task Exchange_after_sso_was_switched_off_is_refused_before_the_code_is_consumed()
    {
        var handler = new ExchangeSsoCodeHandler(store: null!, userManager: null!, tenantStore: null!,
            loginCompletion: null!, options: Disabled());

        var act = () => handler.Handle(new ExchangeSsoCodeCommand("a-code-issued-before-the-switch"), CancellationToken.None);

        (await act.Should().ThrowAsync<NotFoundException>()).WithMessage(SsoLoginFlow.DisabledMessage);
    }
}
