using Identity.Services.Otp;

namespace Identity.Domains.Verification.Features.ValidateVerificationCode;

/// <summary>Kept in the Identity project: the purpose enum is a Notification.Contracts type.</summary>
public sealed record ValidateVerificationCodeCommand(
    Guid UserId,
    VerificationPurpose Purpose,
    string Code) : ICommand<OtpVerificationResult>;

public sealed class ValidateVerificationCodeCommandValidator : AbstractValidator<ValidateVerificationCodeCommand>
{
    public ValidateVerificationCodeCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Purpose).IsInEnum();
        RuleFor(x => x.Code).NotNull();
    }
}

/// <summary>Verifies (and consumes) a code issued by <c>SendVerificationCodeHandler</c>.</summary>
public sealed class ValidateVerificationCodeHandler(IOtpCodeService otpCodeService)
    : ICommandHandler<ValidateVerificationCodeCommand, OtpVerificationResult>
{
    public Task<OtpVerificationResult> Handle(ValidateVerificationCodeCommand command, CancellationToken cancellationToken)
        => otpCodeService.VerifyAsync(command.UserId, command.Purpose.ToOtpPurpose(), command.Code, cancellationToken);
}
