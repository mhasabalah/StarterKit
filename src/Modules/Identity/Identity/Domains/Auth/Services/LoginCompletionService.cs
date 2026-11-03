using Identity.Contracts.Auth.Commands;
using Identity.Domains.Auth.Mappings;
using Identity.Options;
using Identity.Services.Sessions;
using Identity.Services.TwoFactor;
using Microsoft.Extensions.Options;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Services;

/// <summary>
/// The last steps of every interactive login, shared by password login and the 2FA verify step:
/// <list type="bullet">
/// <item><see cref="ChallengeAsync"/>: first factor OK but 2FA is enabled - return a challenge token, no session.</item>
/// <item><see cref="CompleteAsync"/>: apply the login policy (partial login), issue the session, stamp LastLoginAt.</item>
/// </list>
/// </summary>
public interface ILoginCompletionService
{
    Task<LoginResult> ChallengeAsync(ApplicationUser user, CancellationToken ct = default);

    Task<LoginResult> CompleteAsync(ApplicationUser user, bool multiFactor, CancellationToken ct = default);
}

public sealed class LoginCompletionService(
    UserManager<ApplicationUser> userManager,
    IAuthSessionService sessionService,
    ITwoFactorChallengeService challengeService,
    IHttpContextService httpContextService,
    IOptions<LoginPolicyOptions> loginPolicyOptions,
    TimeProvider timeProvider,
    ILogger<LoginCompletionService> logger) : ILoginCompletionService
{
    public const string TwoFactorRequiredMessage = "Two-factor authentication required";

    private readonly LoginPolicyOptions _loginPolicy = loginPolicyOptions.Value;

    public async Task<LoginResult> ChallengeAsync(ApplicationUser user, CancellationToken ct = default)
    {
        var challenge = challengeService.Create(user);
        var roles = await userManager.GetRolesAsync(user);

        logger.LogInformation("User {UserId} passed the first factor; two-factor challenge issued", user.Id);

        return new LoginResult(null, null, null, AuthUserDtoFactory.Create(user, roles), TwoFactorRequiredMessage)
        {
            RequiresTwoFactor = true,
            TwoFactorChallengeToken = challenge.Token,
            TwoFactorChallengeExpiresAt = challenge.ExpiresAt
        };
    }

    public async Task<LoginResult> CompleteAsync(ApplicationUser user, bool multiFactor, CancellationToken ct = default)
    {
        var (shouldBlockLogin, blockReason) = CheckVerificationRequirements(user);

        if (shouldBlockLogin && _loginPolicy.AllowPartialLogin)
        {
            var roles = await userManager.GetRolesAsync(user);
            logger.LogWarning("User {UserId} logged in without tokens: {Reason}", user.Id, blockReason);
            return new LoginResult(null, null, null, AuthUserDtoFactory.Create(user, roles), blockReason!);
        }

        var session = await sessionService.IssueAsync(user, httpContextService.GetIpAddress(), multiFactor, ct);

        user.LastLoginAt = timeProvider.GetUtcNow().UtcDateTime;
        await userManager.UpdateAsync(user);

        logger.LogInformation("User {UserId} logged in (mfa: {MultiFactor})", user.Id, multiFactor);

        return new LoginResult(
            session.AccessToken,
            session.RefreshToken,
            session.AccessTokenExpiresAt,
            AuthUserDtoFactory.Create(user, session.Roles),
            shouldBlockLogin ? $"Login successful. {blockReason}" : "Login successful");
    }

    private (bool ShouldBlock, string? Reason) CheckVerificationRequirements(ApplicationUser user)
    {
        if (_loginPolicy.RequireEmailVerification && !user.EmailConfirmed)
        {
            return (true, "Please verify your email address to access all features.");
        }

        if (_loginPolicy.RequirePhoneVerification && !user.IsPhoneNumberVerified)
        {
            return (true, "Please verify your phone number to access all features.");
        }

        if (_loginPolicy.RequireProfileCompletion && !user.IsProfileComplete)
        {
            return (true, "Please complete your profile to access all features.");
        }

        return (false, null);
    }
}
