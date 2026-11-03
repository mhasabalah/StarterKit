namespace Identity.Services.Sms;

public interface ISmsService
{
    Task SendVerificationCodeAsync(string phoneNumber, string code);
}

/// <summary>
/// Development/Testing only. Sends nothing and never logs the code (codes in logs = account takeover for anyone
/// with log access). Read codes from a debugger or a test double instead.
/// </summary>
public class MockSmsService(ILogger<MockSmsService> logger) : ISmsService
{
    public Task SendVerificationCodeAsync(string phoneNumber, string code)
    {
        logger.LogInformation("Mock SMS: verification code sent (not delivered; code not logged)");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Used outside Development when <c>SmsSettings:Provider</c> is <c>Mock</c>: there is no real SMS channel,
/// so any attempt to send fails loudly instead of silently pretending success.
/// </summary>
public class DisabledSmsService : ISmsService
{
    public Task SendVerificationCodeAsync(string phoneNumber, string code) =>
        throw new InvalidOperationException(
            "No SMS provider is configured. Set SmsSettings:Provider to an implemented provider.");
}
