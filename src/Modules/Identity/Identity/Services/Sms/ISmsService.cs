namespace Identity.Services.Sms;

public interface ISmsService
{
    Task SendVerificationCodeAsync(string phoneNumber, string code);
}

public class MockSmsService(ILogger<MockSmsService> logger) : ISmsService
{
    public Task SendVerificationCodeAsync(string phoneNumber, string code)
    {
        logger.LogInformation("📱 SMS Verification Code: {Code} sent to {PhoneNumber}", code, phoneNumber);
        return Task.CompletedTask;
    }
}
