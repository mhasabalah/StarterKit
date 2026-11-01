using Hangfire;
using Identity.Contracts.Onboarding.Commands;
using Identity.Data.Repository.Onboarding;
using Identity.Domains.Onboarding.Jobs;
using Identity.Domains.Onboarding.Models;
using Identity.Options;
using Microsoft.Extensions.Options;
using Shared.Notification.Contracts;
using Shared.Notification.Enums;

namespace Identity.Domains.Onboarding.Features.ResendOtp;

public class ResendOnboardingOtpCommandValidator : AbstractValidator<ResendOnboardingOtpCommand>
{
    public ResendOnboardingOtpCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("UserId is required");
    }
}

public class ResendOnboardingOtpHandler(
    IOnboardingProcessRepository repository,
    IVerificationService verificationService,
    IBackgroundJobClient backgroundJobClient,
    IOptions<OnboardingOptions> options,
    ILogger<ResendOnboardingOtpHandler> logger)
    : ICommandHandler<ResendOnboardingOtpCommand, ResendOnboardingOtpResult>
{
    private readonly OnboardingOptions _onboardingOptions = options.Value;

    public async Task<ResendOnboardingOtpResult> Handle(
        ResendOnboardingOtpCommand command,
        CancellationToken cancellationToken)
    {
        var maxResends = _onboardingOptions.MaxResends <= 0 ? 3 : _onboardingOptions.MaxResends;
        var process = await repository.GetActiveByUserIdAsync(command.UserId, cancellationToken);
        if (process == null)
        {
            var latestProcess = await repository.GetLatestByUserIdAsync(command.UserId, cancellationToken);
            if (latestProcess?.Status == OnboardingStatus.TimedOut)
            {
                throw new BadRequestException("Onboarding has expired. Please register again.");
            }

            throw new NotFoundException("OnboardingProcess", command.UserId);
        }

        if (process.Status == OnboardingStatus.Registered)
        {
            var initialSendResult = await verificationService.SendCodeAsync(
                command.UserId,
                VerificationPurpose.PhoneVerification,
                NotificationCulture.En,
                cancellationToken);

            if (!initialSendResult.Success)
            {
                logger.LogError(
                    "Failed to send onboarding OTP for registered process and user {UserId}. Error: {Error}",
                    command.UserId,
                    initialSendResult.Error);
                throw new BadRequestException("Failed to send verification code. Please try again.");
            }

            var timeoutMinutes = _onboardingOptions.TimeoutMinutes <= 0 ? 60 : _onboardingOptions.TimeoutMinutes;
            var jobId = backgroundJobClient.Schedule<OnboardingTimeoutJob>(
                job => job.ExecuteAsync(command.UserId, CancellationToken.None),
                TimeSpan.FromMinutes(timeoutMinutes));

            process.MarkOtpSent(jobId);
            await repository.SaveChangesAsync(cancellationToken);

            return new ResendOnboardingOtpResult(
                "Verification code sent successfully.",
                maxResends - process.ResendCount);
        }

        if (process.Status != OnboardingStatus.OtpSent)
        {
            throw new BadRequestException("Cannot resend OTP in current state.");
        }

        // IncrementResendCount enforces the limit internally — no duplicate check needed here.
        process.IncrementResendCount(maxResends);

        var sendResult = await verificationService.SendCodeAsync(
            command.UserId,
            VerificationPurpose.PhoneVerification,
            NotificationCulture.En,
            cancellationToken);

        if (!sendResult.Success)
        {
            logger.LogError(
                "Failed to resend onboarding OTP for user {UserId}. Error: {Error}",
                command.UserId, sendResult.Error);
            throw new BadRequestException("Failed to send verification code. Please try again.");
        }

        await repository.SaveChangesAsync(cancellationToken);

        return new ResendOnboardingOtpResult(
            "Verification code sent successfully.",
            maxResends - process.ResendCount);
    }
}
