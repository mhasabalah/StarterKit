namespace Identity.Contracts.Auth.Commands;

public record VerifyResetOtpCommand(
    string PhoneNumber,
    string OtpCode
) : ICommand<VerifyResetOtpResult>;

public record VerifyResetOtpResult(
    Guid UserId,
    string ResetToken,
    string Message
);
