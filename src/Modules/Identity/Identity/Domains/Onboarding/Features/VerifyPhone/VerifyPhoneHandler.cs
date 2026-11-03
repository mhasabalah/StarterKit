using Hangfire;
using Identity.Contracts.Onboarding.Commands;
using Identity.Domains.Auth.Mappings;
using Identity.Options;
using Identity.Services.Otp;
using Identity.Services.Sessions;
using Microsoft.Extensions.Options;
using Shared.Core.Web;

namespace Identity.Domains.Onboarding.Features.VerifyPhone;

public class VerifyOnboardingPhoneCommandValidator : AbstractValidator<VerifyOnboardingPhoneCommand>
{
    public VerifyOnboardingPhoneCommandValidator(IOptions<PhoneVerificationOptions> phoneOptions)
    {
        var codeLength = phoneOptions.Value.CodeLength;

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Verification code is required")
            .Length(codeLength).WithMessage($"Verification code must be {codeLength} characters.");
    }
}

/// <summary>
/// Onboarding is the one documented flow that signs the user in after a phone code: the code is checked by the
/// unified <see cref="IOtpCodeService"/> (purpose OnboardingPhoneVerification: attempt limit, expiry, single use) and the session is issued by
/// <see cref="IAuthSessionService"/> (persisted, rotating refresh token).
/// </summary>
public class VerifyOnboardingPhoneHandler(
    IOnboardingProcessRepository repository,
    UserManager<ApplicationUser> userManager,
    IOtpCodeService otpCodeService,
    IAuthSessionService sessionService,
    IHttpContextService httpContextService,
    IBackgroundJobClient backgroundJobClient,
    ILogger<VerifyOnboardingPhoneHandler> logger)
    : ICommandHandler<VerifyOnboardingPhoneCommand, VerifyOnboardingPhoneResult>
{
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

        var verification = await otpCodeService.VerifyAsync(
            command.UserId,
            OtpPurpose.OnboardingPhoneVerification,
            command.Code,
            cancellationToken);

        switch (verification.Status)
        {
            case OtpVerificationStatus.Success:
                break;
            case OtpVerificationStatus.Expired:
                throw new BadRequestException("Verification code has expired. Please request a new one.");
            case OtpVerificationStatus.Locked:
                throw new BadRequestException("Too many failed attempts. Please request a new code.");
            default:
                throw new BadRequestException("Invalid verification code.");
        }

        // Step 1: commit phone-verified status first. The race with the timeout job is decided here via
        // optimistic concurrency: if the timeout committed first, no user change and no tokens.
        process.MarkPhoneVerified();
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BadRequestException("Onboarding has expired. Please register again.");
        }

        // Step 2: mark the user's phone as verified.
        user.IsPhoneNumberVerified = true;
        user.UpdatedAt = DateTime.UtcNow;
        user.UpdateProfileCompletion();

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
            throw new BadRequestException($"Failed to update user verification status: {errors}");
        }

        // Step 3: issue the session (the refresh token is persisted by the session service).
        var session = await sessionService.IssueAsync(user, httpContextService.GetIpAddress(), cancellationToken);

        // Step 4: cancel the timeout job only after all DB writes succeeded.
        if (!string.IsNullOrWhiteSpace(process.TimeoutHangfireJobId))
            backgroundJobClient.Delete(process.TimeoutHangfireJobId);

        logger.LogInformation("User {UserId} onboarding phone verified successfully", command.UserId);

        return new VerifyOnboardingPhoneResult(
            session.AccessToken,
            session.RefreshToken,
            session.AccessTokenExpiresAt,
            AuthUserDtoFactory.Create(user, session.Roles));
    }
}
