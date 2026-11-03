using Identity.Services.Otp;

namespace Identity.Domains.Verification.Features.SendVerificationCode;

/// <summary>
/// Kept in the Identity project (not Identity.Contracts) because the purpose/culture enums are Notification.Contracts
/// types; nothing outside this module sends it.
/// </summary>
public sealed record SendVerificationCodeCommand(
    Guid UserId,
    VerificationPurpose Purpose,
    NotificationCulture Culture) : ICommand<OtpSendResult>;

public sealed class SendVerificationCodeCommandValidator : AbstractValidator<SendVerificationCodeCommand>
{
    public SendVerificationCodeCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Purpose).IsInEnum();
        RuleFor(x => x.Culture).IsInEnum();
    }
}

/// <summary>Issues a code with the unified Identity OTP service and has Notification deliver it.</summary>
public sealed class SendVerificationCodeHandler(IOtpSender otpSender)
    : ICommandHandler<SendVerificationCodeCommand, OtpSendResult>
{
    public Task<OtpSendResult> Handle(SendVerificationCodeCommand command, CancellationToken cancellationToken)
        => otpSender.SendAsync(command.UserId, command.Purpose.ToOtpPurpose(), command.Culture, cancellationToken);
}
