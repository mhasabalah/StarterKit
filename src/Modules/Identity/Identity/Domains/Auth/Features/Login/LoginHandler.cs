using Identity.Contracts.Auth.Commands;
using Identity.Domains.Auth.Services;
using Identity.Services.Phone;
using Identity.Services.Sso;

namespace Identity.Domains.Auth.Features.Login;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty()
            .WithMessage("Email or phone number is required");
        RuleFor(x => x.Password).NotEmpty()
            .WithMessage("Password is required");
    }
}

/// <summary>
/// Password login. When the account has 2FA enabled the password alone never yields a session: the result carries
/// <c>RequiresTwoFactor</c> + a challenge token for <c>POST /api/auth/2fa/verify</c>.
/// </summary>
public class LoginHandler(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ILoginCompletionService loginCompletion,
    IPhoneNumberNormalizer phoneNormalizer,
    ISsoEnforcementPolicy ssoPolicy,
    ILogger<LoginHandler> logger)
    : ICommandHandler<LoginCommand, LoginResult>
{
    /// <summary>Same message for unknown user and wrong password (no account enumeration).</summary>
    public const string InvalidCredentialsMessage = "Invalid credentials";

    private static readonly ApplicationUser DummyUser = new();
    private static string? _dummyPasswordHash;

    public async Task<LoginResult> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await FindUserByIdentifierAsync(command.Identifier, cancellationToken);

        if (user is null)
        {
            // Spend the same time as a real password check so response timing does not reveal the account.
            RunDummyPasswordCheck(command.Password);
            logger.LogWarning("Login failed: unknown identifier");
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, command.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            // Same answer as a wrong password: a distinct "locked out" message would confirm to whoever caused the
            // lockout (5 guesses at a stranger's email) that the account exists. The log keeps the real reason.
            logger.LogWarning("Login failed: user {UserId} is locked out", user.Id);
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        if (!result.Succeeded)
        {
            logger.LogWarning("Login failed: invalid password for user {UserId}", user.Id);
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Login failed: user {UserId} is deactivated", user.Id);
            throw new UnauthorizedException("Account is disabled.");
        }

        // Checked only AFTER the password matched, so it never tells a stranger anything about the account.
        await ssoPolicy.EnsureNonSsoSignInAllowedAsync(user, cancellationToken);

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            return await loginCompletion.ChallengeAsync(user, cancellationToken);
        }

        return await loginCompletion.CompleteAsync(user, multiFactor: false, cancellationToken);
    }

    private void RunDummyPasswordCheck(string password)
    {
        _dummyPasswordHash ??= userManager.PasswordHasher.HashPassword(DummyUser, Guid.NewGuid().ToString("N"));
        userManager.PasswordHasher.VerifyHashedPassword(DummyUser, _dummyPasswordHash, password);
    }

    private async Task<ApplicationUser?> FindUserByIdentifierAsync(string identifier, CancellationToken ct)
    {
        if (identifier.Contains('@'))
        {
            return await userManager.FindByEmailAsync(identifier.Trim());
        }

        var phone = phoneNormalizer.Normalize(identifier);
        return phone is null ? null : await userManager.FindByPhoneAsync(phone, ct);
    }
}
