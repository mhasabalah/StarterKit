using Hangfire;
using Identity.Contracts.Auth.Dtos;
using Identity.Contracts.Onboarding.Commands;
using Identity.Models;
using Identity.Options;
using Identity.Services.Token;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Identity.Domains.Onboarding.Features.VerifyPhone;

public class VerifyOnboardingPhoneCommandValidator : AbstractValidator<VerifyOnboardingPhoneCommand>
{
    public VerifyOnboardingPhoneCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required")
            .Length(6).WithMessage("Verification code must be 6 characters.");
    }
}

public class VerifyOnboardingPhoneHandler(
    IOnboardingProcessRepository repository,
    UserManager<ApplicationUser> userManager,
    IVerificationService verificationService,
    IJwtTokenGenerator tokenGenerator,
    ApplicationIdentityDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    IOptions<JwtOptions> jwtOptions,
    IBackgroundJobClient backgroundJobClient,
    ILogger<VerifyOnboardingPhoneHandler> logger)
    : ICommandHandler<VerifyOnboardingPhoneCommand, VerifyOnboardingPhoneResult>
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<VerifyOnboardingPhoneResult> Handle(
        VerifyOnboardingPhoneCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user == null)
            throw new NotFoundException("User", command.UserId);

        var process = await repository.GetActiveByUserIdAsync(command.UserId, cancellationToken);
        if (process == null)
        {
            var latestProcess = await repository.GetLatestByUserIdAsync(command.UserId, cancellationToken);
            if (latestProcess?.Status == OnboardingStatus.TimedOut)
                throw new BadRequestException("Onboarding has expired. Please register again.");

            throw new NotFoundException("OnboardingProcess", command.UserId);
        }

        if (process.Status != OnboardingStatus.OtpSent)
            throw new BadRequestException("Phone verification is not available in current state.");

        var verificationResult = await verificationService.ValidateCodeAsync(
            command.UserId,
            command.Code,
            VerificationPurpose.PhoneVerification,
            cancellationToken);

        switch (verificationResult)
        {
            case VerificationResult.NotFound:
            case VerificationResult.Mismatch:
                throw new BadRequestException("Invalid verification code.");
            case VerificationResult.Expired:
                throw new BadRequestException("Verification code has expired. Please request a new one.");
            case VerificationResult.AlreadyUsed:
                throw new BadRequestException("Verification code has already been used.");
            case VerificationResult.MaxAttemptsReached:
                throw new BadRequestException("Too many failed attempts. Please request a new code.");
            case VerificationResult.Success:
                break;
            default:
                throw new BadRequestException("Invalid verification code.");
        }

        // Step 1: Commit phone-verified status first — this is where the race with the
        // timeout job is decided via optimistic concurrency (xmin).
        // If the timeout job committed first, xmin has changed and DbUpdateConcurrencyException
        // is thrown here, before we touch the user entity or issue any tokens.
        process.MarkPhoneVerified();
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BadRequestException("Onboarding has expired. Please register again.");
        }

        // Step 2: Mark the user's phone as verified. The process save above already
        // committed, so userManager.UpdateAsync only writes the user row.
        user.IsPhoneNumberVerified = true;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdateProfileCompletion();

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
            throw new BadRequestException($"Failed to update user verification status: {errors}");
        }

        // Step 3: Issue tokens and persist the refresh token.
        var accessToken = await tokenGenerator.GenerateTokenAsync(user);
        var refreshTokenString = tokenGenerator.GenerateRefreshToken();
        var refreshTokenHash = ComputeTokenHash(refreshTokenString);
        var ipAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        dbContext.RefreshTokens.Add(refreshToken);
        await repository.SaveChangesAsync(cancellationToken);

        // Step 4: Cancel the timeout job only after all DB writes have succeeded.
        // Cancelling before the saves risked deleting the job while leaving the process
        // in OtpSent with no recovery path if a subsequent save failed.
        if (!string.IsNullOrWhiteSpace(process.TimeoutHangfireJobId))
            backgroundJobClient.Delete(process.TimeoutHangfireJobId);

        logger.LogInformation("User {UserId} onboarding phone verified successfully", command.UserId);

        var roles = await userManager.GetRolesAsync(user);
        var userDto = CreateUserDto(user, roles);

        return new VerifyOnboardingPhoneResult(
            accessToken,
            refreshTokenString,
            DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes),
            userDto);
    }

    private static string ComputeTokenHash(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }

    private static UserDto CreateUserDto(ApplicationUser user, IList<string> roles)
    {
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            PhoneNumber = user.PhoneNumber,
            FirstName = user.FirstName,
            LastName = user.LastName,
            IsEmailVerified = user.EmailConfirmed,
            IsPhoneVerified = user.IsPhoneNumberVerified,
            IsProfileComplete = user.IsProfileComplete,
            Roles = roles.ToList()
        };
    }
}
